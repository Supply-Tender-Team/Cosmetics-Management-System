using System.Security.Cryptography;
using Fido2NetLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Biometrics;
using TenderSystem.Infrastructure.Options;
using TenderSystem.Services.Biometrics;

namespace TenderSystem.Tests.Web;

/// <summary>
/// The rules of face and fingerprint sign-in that need no camera and no device: a challenge works once, who it belongs to, the throttling,
/// how many face templates a person keeps, the match thresholds, and which site address a passkey is tied to.
/// (The camera analysis itself needs the AI model files, and the fingerprint signature needs a real device: try those by hand.)
/// </summary>
public class BiometricsTests
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static (FaceService Face, ApplicationDbContext Db, FaceTemplateProtector Protector) CreateFace(Action<FaceOptions>? tweak = null)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = new FaceOptions { PassiveLiveness = false, TemplateEncryptionKey = Key };
        tweak?.Invoke(options);
        var protector = new FaceTemplateProtector(Options.Create(options), new AccountTestKit.TestEnvironment("unused", false));
        // the camera engine (heavy AI model files) is not needed for these rules, so it is left empty
        var face = new FaceService(null!, protector, db, new MemoryCache(new MemoryCacheOptions()), Options.Create(options), NullLogger<FaceService>.Instance);
        return (face, db, protector);
    }

    private static List<FaceFrameDto> Frames(int count) => Enumerable.Range(0, count).Select(_ => new FaceFrameDto { Step = "center", Image = "" }).ToList();

    // ---------------------------------------------------------------- face: the challenge

    [Fact]
    public void Login_asks_to_look_straight_and_turn_to_one_side_and_enrolment_asks_for_both_sides()
    {
        var (face, _, _) = CreateFace();

        Assert.Equal(2, face.CreateChallenge("login", "a@test.com").Steps.Length);
        Assert.Equal(3, face.CreateChallenge("enroll", "user-1").Steps.Length);                 // a saved template needs both sides
        Assert.Equal(3, CreateFace(o => o.LoginTurns = 2).Face.CreateChallenge("login", "a@test.com").Steps.Length);
    }

    [Fact]
    public async Task A_challenge_can_be_used_only_once()
    {
        var (face, _, _) = CreateFace();
        var challenge = face.CreateChallenge("login", "a@test.com");

        var first = await face.AnalyzeAsync(challenge.ChallengeId, "login", "a@test.com", Frames(1));
        var second = await face.AnalyzeAsync(challenge.ChallengeId, "login", "a@test.com", Frames(10));

        Assert.Contains("did not capture enough images", first.Reason);          // it was accepted, then the too few frames were refused
        Assert.Contains("expired", second.Reason);                               // but the challenge is used up, even though the first try failed
        Assert.False(second.Ok);
    }

    [Theory]
    [InlineData("enroll", "a@test.com")]        // another purpose
    [InlineData("login", "other@test.com")]     // another person
    public async Task A_challenge_works_only_for_the_purpose_and_the_person_it_was_made_for(string purpose, string subject)
    {
        var (face, _, _) = CreateFace();
        var challenge = face.CreateChallenge("login", "a@test.com");

        var result = await face.AnalyzeAsync(challenge.ChallengeId, purpose, subject, Frames(10));

        Assert.False(result.Ok);
        Assert.Contains("expired", result.Reason);
    }

    [Fact]
    public async Task An_unknown_or_empty_challenge_is_refused()
    {
        var (face, _, _) = CreateFace();

        Assert.Contains("expired", (await face.AnalyzeAsync("no-such-id", "login", "a@test.com", Frames(10))).Reason);
        Assert.Contains("expired", (await face.AnalyzeAsync("", "login", "a@test.com", Frames(10))).Reason);
    }

    // ---------------------------------------------------------------- face: throttling

    [Fact]
    public void Too_many_failures_from_one_address_block_it_and_a_success_clears_them()
    {
        var (face, _, _) = CreateFace(o => o.IdentifyMaxFailures = 3);

        for (var i = 0; i < 2; i++) face.RegisterFailure("1.2.3.4");
        Assert.False(face.IsThrottled("1.2.3.4"));
        face.RegisterFailure("1.2.3.4");
        Assert.True(face.IsThrottled("1.2.3.4"));
        Assert.False(face.IsThrottled("9.9.9.9"));                                // other addresses are not affected

        face.ClearFailures("1.2.3.4");
        Assert.False(face.IsThrottled("1.2.3.4"));
    }

    // ---------------------------------------------------------------- face: saved templates

    [Fact]
    public async Task A_person_keeps_at_most_three_face_templates_and_the_oldest_is_retired()
    {
        var (face, db, _) = CreateFace();
        for (var i = 0; i < 4; i++)
        {
            await face.EnrollAsync("user-1", [1f, 0f, 0f, i], 0.9);
            await Task.Delay(5);                                                  // different creation times, so "oldest" is clear
        }

        var rows = await db.FaceEnrollments.OrderBy(e => e.CreatedAt).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.False(rows[0].IsActive);
        Assert.All(rows.Skip(1), r => Assert.True(r.IsActive));
        Assert.Equal(3, await face.CountAsync("user-1"));
    }

    [Fact]
    public void A_saved_template_is_encrypted_and_a_changed_or_wrongly_keyed_one_is_refused()
    {
        var (_, _, protector) = CreateFace();
        float[] embedding = [0.1f, 0.2f, 0.3f, 0.4f];

        var blob = protector.Protect(embedding);
        Assert.Equal(embedding, protector.Unprotect(blob));
        Assert.NotEqual(blob, protector.Protect(embedding));                      // a fresh random value each time

        var changed = (byte[])blob.Clone();
        changed[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(changed));

        var otherKey = new FaceTemplateProtector(Options.Create(new FaceOptions { TemplateEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }),
            new AccountTestKit.TestEnvironment("unused", false));
        Assert.ThrowsAny<CryptographicException>(() => otherKey.Unprotect(blob));
    }

    [Fact]
    public void A_missing_key_is_an_error_outside_development()
    {
        var protector = new FaceTemplateProtector(Options.Create(new FaceOptions { TemplateEncryptionKey = "CHANGE_ME" }), new AccountTestKit.TestEnvironment("unused", false));

        Assert.Throws<InvalidOperationException>(() => protector.Protect([1f]));
    }

    // ---------------------------------------------------------------- face: who is this?

    [Fact]
    public async Task A_face_matches_only_its_own_active_templates_of_the_current_model()
    {
        var (face, db, protector) = CreateFace();
        await face.EnrollAsync("user-1", [1f, 0f, 0f, 0f], 0.9);
        db.FaceEnrollments.Add(new FaceEnrollment { UserId = "user-1", FaceTemplate = protector.Protect([0f, 1f, 0f, 0f]), ModelVersion = "an-old-model", IsActive = true });
        db.FaceEnrollments.Add(new FaceEnrollment { UserId = "user-1", FaceTemplate = protector.Protect([0f, 0f, 1f, 0f]), ModelVersion = FaceEngine.ModelVersion, IsActive = false });
        await db.SaveChangesAsync();

        Assert.True((await face.MatchAsync("user-1", [1f, 0f, 0f, 0f])).Ok);          // the real template
        Assert.False((await face.MatchAsync("user-1", [0f, 1f, 0f, 0f])).Ok);         // only matches the old-model template: ignored
        Assert.False((await face.MatchAsync("user-1", [0f, 0f, 1f, 0f])).Ok);         // only matches the retired template: ignored
        Assert.False((await face.MatchAsync("user-2", [1f, 0f, 0f, 0f])).Ok);         // someone else's face, no templates
    }

    [Fact]
    public async Task Identify_names_a_person_only_when_it_is_clearly_them()
    {
        var (face, _, _) = CreateFace();
        await face.EnrollAsync("alice", [1f, 0f, 0f, 0f], 0.9);
        await face.EnrollAsync("bob", [0f, 1f, 0f, 0f], 0.9);

        var alice = await face.IdentifyAsync([1f, 0f, 0f, 0f]);
        Assert.True(alice.Ok);
        Assert.Equal("alice", alice.UserId);

        var nobody = await face.IdentifyAsync([0f, 0f, 1f, 0f]);                       // a stranger
        Assert.False(nobody.Ok);
        Assert.Null(nobody.UserId);
    }

    [Fact]
    public async Task Identify_refuses_when_two_accounts_have_the_same_face()
    {
        var (face, _, _) = CreateFace();
        await face.EnrollAsync("twin-1", [1f, 0f, 0f, 0f], 0.9);
        await face.EnrollAsync("twin-2", [1f, 0f, 0f, 0f], 0.9);

        var result = await face.IdentifyAsync([1f, 0f, 0f, 0f]);

        Assert.False(result.Ok);                                                      // ambiguous: it must not pick one of them
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task Identify_finds_nobody_when_no_face_is_enrolled()
    {
        var (face, _, _) = CreateFace();

        Assert.False((await face.IdentifyAsync([1f, 0f, 0f, 0f])).Ok);
    }

    // ---------------------------------------------------------------- fingerprint (passkey)

    private static (PasskeyService Passkeys, ApplicationDbContext Db) CreatePasskeys(bool development)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var options = new WebAuthnOptions { RelyingPartyId = "example.com", Origins = ["https://example.com"], MaxFailuresPer10Minutes = 3 };
        return (new PasskeyService(db, new MemoryCache(new MemoryCacheOptions()), Options.Create(options),
            new AccountTestKit.TestEnvironment("unused", development), NullLogger<PasskeyService>.Instance), db);
    }

    private static string RpIdOf(PasskeyBegin begin) => begin.Options.GetProperty("rpId").GetString()!;

    [Fact]
    public void In_development_the_passkey_belongs_to_the_address_in_the_browser()
    {
        var (passkeys, _) = CreatePasskeys(development: true);

        var begin = passkeys.BeginLogin(new PasskeyOptionsRequest { Origin = "https://my-tunnel.example.dev" });

        Assert.Equal("my-tunnel.example.dev", RpIdOf(begin));
    }

    [Fact]
    public void Outside_development_the_address_sent_by_the_browser_is_never_trusted()
    {
        var (passkeys, _) = CreatePasskeys(development: false);

        var begin = passkeys.BeginLogin(new PasskeyOptionsRequest { Origin = "https://evil.example" });

        Assert.Equal("example.com", RpIdOf(begin));                                   // only the fixed PUBLIC_URL setting counts
    }

    [Fact]
    public async Task A_passkey_login_challenge_works_once_and_an_unknown_one_is_refused()
    {
        var (passkeys, _) = CreatePasskeys(development: false);
        var begin = passkeys.BeginLogin(new PasskeyOptionsRequest());
        var request = new PasskeyLoginRequest { OptionsId = begin.OptionsId, Credential = new AuthenticatorAssertionRawResponse { RawId = [9, 9, 9] } };

        var first = await passkeys.CompleteLoginAsync(request);
        var second = await passkeys.CompleteLoginAsync(request);
        var unknown = await passkeys.CompleteLoginAsync(new PasskeyLoginRequest { OptionsId = "nope", Credential = request.Credential });

        Assert.Contains("not recognised", first.Message);                             // the challenge was fine, but no such passkey is saved
        Assert.Null(first.UserId);
        Assert.Contains("expired", second.Message);                                   // the same challenge a second time
        Assert.Contains("expired", unknown.Message);
    }

    [Fact]
    public async Task A_revoked_passkey_cannot_sign_in()
    {
        var (passkeys, db) = CreatePasskeys(development: false);
        db.BiometricCredentials.Add(new BiometricCredential { UserId = "u1", CredentialId = [1, 2, 3], PublicKey = [1], DeviceName = "Phone", IsRevoked = true });
        await db.SaveChangesAsync();
        var begin = passkeys.BeginLogin(new PasskeyOptionsRequest());

        var result = await passkeys.CompleteLoginAsync(new PasskeyLoginRequest { OptionsId = begin.OptionsId, Credential = new AuthenticatorAssertionRawResponse { RawId = [1, 2, 3] } });

        Assert.False(result.Ok);
        Assert.Null(result.UserId);
    }

    [Fact]
    public async Task A_person_can_remove_only_their_own_fingerprint_devices()
    {
        var (passkeys, db) = CreatePasskeys(development: false);
        db.BiometricCredentials.AddRange(
            new BiometricCredential { Id = 1, UserId = "u1", CredentialId = [1], PublicKey = [1], DeviceName = "Phone" },
            new BiometricCredential { Id = 2, UserId = "u2", CredentialId = [2], PublicKey = [1], DeviceName = "Laptop" });
        await db.SaveChangesAsync();

        Assert.False(await passkeys.RemoveAsync("u1", 2));                            // someone else's device
        Assert.True(await passkeys.RemoveAsync("u1", 1));
        Assert.Equal(0, await passkeys.CountAsync("u1"));
        Assert.Equal(1, await passkeys.CountAsync("u2"));
        Assert.Empty(await passkeys.ListAsync("u1"));                                 // removed devices are not listed
    }

    [Fact]
    public void Too_many_failed_fingerprint_attempts_from_one_address_block_it()
    {
        var (passkeys, _) = CreatePasskeys(development: false);

        for (var i = 0; i < 3; i++) passkeys.RegisterFailure("1.2.3.4");

        Assert.True(passkeys.IsThrottled("1.2.3.4"));
        Assert.False(passkeys.IsThrottled("5.6.7.8"));
        passkeys.ClearFailures("1.2.3.4");
        Assert.False(passkeys.IsThrottled("1.2.3.4"));
    }
}
