using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Controllers;
using TenderSystem.Data.Entities.Biometrics;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Audit;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Tests.Web;

/// <summary>Seeing and removing your own saved faces (Profile &amp; security): the service rules and the "keep one way to sign in" rule of FaceController.Remove.</summary>
public class FaceRemovalTests
{
    private sealed class Setup
    {
        public required AccountTestKit Kit { get; init; }
        public required FaceService Face { get; init; }
        public required PasskeyService Passkeys { get; init; }
        public required FaceController Controller { get; init; }
        public required ApplicationUser User { get; init; }
    }

    private static async Task<Setup> CreateAsync(string onboarding = "Completed")
    {
        var kit = new AccountTestKit();
        var options = new FaceOptions { PassiveLiveness = false, TemplateEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var env = new AccountTestKit.TestEnvironment("unused", false);
        var face = new FaceService(null!, new FaceTemplateProtector(Options.Create(options), env), kit.Db, new MemoryCache(new MemoryCacheOptions()),
            Options.Create(options), NullLogger<FaceService>.Instance);
        var passkeys = new PasskeyService(kit.Db, new MemoryCache(new MemoryCacheOptions()), Options.Create(new WebAuthnOptions()), env, NullLogger<PasskeyService>.Instance);

        var user = await kit.AddUserAsync("me@test.com", Roles.Requester);
        user.OnboardingStatus = onboarding;
        await kit.Users.UpdateAsync(user);

        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test")) };
        var controller = new FaceController(face, passkeys, kit.Users, new AuditService(kit.Db, new HttpContextAccessor()))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return new Setup { Kit = kit, Face = face, Passkeys = passkeys, Controller = controller, User = user };
    }

    private static async Task<int> AddFaceAsync(Setup s, string userId, bool active = true)
    {
        await s.Face.EnrollAsync(userId, [1f, 0f, 0f, 0f], 0.9);
        await Task.Delay(5);
        var row = await s.Kit.Db.FaceEnrollments.Where(e => e.UserId == userId).OrderByDescending(e => e.CreatedAt).FirstAsync();
        if (!active) { row.IsActive = false; await s.Kit.Db.SaveChangesAsync(); }
        return row.Id;
    }

    private static (bool Ok, string Message) Answer(IActionResult result)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("ok").GetBoolean(), doc.RootElement.GetProperty("message").GetString() ?? "");
    }

    // ---------------------------------------------------------------- the service

    [Fact]
    public async Task The_list_shows_only_my_active_faces_oldest_first()
    {
        var s = await CreateAsync();
        using var kit = s.Kit;
        var first = await AddFaceAsync(s, s.User.Id);
        var second = await AddFaceAsync(s, s.User.Id);
        await AddFaceAsync(s, s.User.Id, active: false);          // a retired one
        await AddFaceAsync(s, "someone-else");

        var list = await s.Face.ListAsync(s.User.Id);

        Assert.Equal([first, second], list.Select(f => f.Id).ToArray());
    }

    [Fact]
    public async Task A_face_is_deleted_for_good_and_only_by_its_owner_and_only_while_active()
    {
        var s = await CreateAsync();
        using var kit = s.Kit;
        var mine = await AddFaceAsync(s, s.User.Id);
        var retired = await AddFaceAsync(s, s.User.Id, active: false);
        var other = await AddFaceAsync(s, "someone-else");

        Assert.False(await s.Face.RemoveAsync(s.User.Id, other));        // someone else's face
        Assert.False(await s.Face.RemoveAsync(s.User.Id, retired));      // already retired
        Assert.True(await s.Face.RemoveAsync(s.User.Id, mine));

        Assert.Null(await kit.Db.FaceEnrollments.FindAsync(mine));        // the encrypted template row is gone, not just switched off
        Assert.NotNull(await kit.Db.FaceEnrollments.FindAsync(other));
    }

    // ---------------------------------------------------------------- the controller rule: keep one way to sign in

    [Fact]
    public async Task The_last_way_to_sign_in_cannot_be_removed_once_onboarding_is_finished()
    {
        var s = await CreateAsync();
        using var kit = s.Kit;
        var only = await AddFaceAsync(s, s.User.Id);

        var (ok, message) = Answer(await s.Controller.Remove(new FaceRemoveRequest { Id = only }));

        Assert.False(ok);
        Assert.Contains("at least one way to sign in", message);
        Assert.Equal(1, await s.Face.CountAsync(s.User.Id));
    }

    [Fact]
    public async Task The_last_face_can_go_when_a_fingerprint_device_is_saved()
    {
        var s = await CreateAsync();
        using var kit = s.Kit;
        var only = await AddFaceAsync(s, s.User.Id);
        kit.Db.BiometricCredentials.Add(new BiometricCredential { UserId = s.User.Id, CredentialId = [1], PublicKey = [1], DeviceName = "Phone" });
        await kit.Db.SaveChangesAsync();

        var (ok, _) = Answer(await s.Controller.Remove(new FaceRemoveRequest { Id = only }));

        Assert.True(ok);
        Assert.Equal(0, await s.Face.CountAsync(s.User.Id));
        Assert.Contains(await kit.Db.AuditLogs.ToListAsync(), a => a.Action == "FaceRemoved" && a.EntityId == s.User.Id);
    }

    [Fact]
    public async Task One_of_two_faces_can_go_without_a_fingerprint()
    {
        var s = await CreateAsync();
        using var kit = s.Kit;
        var first = await AddFaceAsync(s, s.User.Id);
        await AddFaceAsync(s, s.User.Id);

        var (ok, _) = Answer(await s.Controller.Remove(new FaceRemoveRequest { Id = first }));

        Assert.True(ok);
        Assert.Equal(1, await s.Face.CountAsync(s.User.Id));
    }

    [Fact]
    public async Task While_onboarding_is_not_finished_the_face_can_be_removed_to_start_again()
    {
        var s = await CreateAsync(onboarding: "InProgress");
        using var kit = s.Kit;
        var only = await AddFaceAsync(s, s.User.Id);

        var (ok, _) = Answer(await s.Controller.Remove(new FaceRemoveRequest { Id = only }));

        Assert.True(ok);
    }

    [Fact]
    public async Task Another_persons_face_cannot_be_removed_through_the_controller()
    {
        var s = await CreateAsync();
        using var kit = s.Kit;
        await AddFaceAsync(s, s.User.Id);
        await AddFaceAsync(s, s.User.Id);
        var other = await AddFaceAsync(s, "someone-else");

        var (ok, message) = Answer(await s.Controller.Remove(new FaceRemoveRequest { Id = other }));

        Assert.False(ok);
        Assert.Contains("could not be removed", message);
        Assert.NotNull(await kit.Db.FaceEnrollments.FindAsync(other));
    }
}
