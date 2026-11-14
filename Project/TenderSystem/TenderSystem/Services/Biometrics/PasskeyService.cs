using System.Text;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Biometrics;
using TenderSystem.Data.Entities.Identity;
using TenderSystem.Infrastructure;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Biometrics;

// ───────────── what the browser sends / receives ─────────────
public class PasskeyOptionsRequest
{
    /// <summary>window.location.origin of the page. Used in Development only (so localhost and a tunnel both work without editing .env).</summary>
    public string? Origin { get; set; }
    /// <summary>"platform" = this device (Windows Hello, Touch ID, phone fingerprint), "cross-platform" = another device (a phone via QR), null = let the browser ask.</summary>
    public string? Attachment { get; set; }
}
public class PasskeyRegisterRequest
{
    public string OptionsId { get; set; } = "";
    public string? DeviceName { get; set; }
    public AuthenticatorAttestationRawResponse Credential { get; set; } = null!;
}
public class PasskeyLoginRequest
{
    public string OptionsId { get; set; } = "";
    public AuthenticatorAssertionRawResponse Credential { get; set; } = null!;
}
public class PasskeyRemoveRequest { public int Id { get; set; } }

public record PasskeyBegin(string OptionsId, JsonElement Options);
public record PasskeyResult(bool Ok, string Message, string? UserId = null, int Count = 0);
public record PasskeyDevice(int Id, string DeviceName, string AuthenticatorType, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>
/// Fingerprint (and face / PIN of the device) sign-in with WebAuthn passkeys, built on the Fido2NetLib library.
///
/// How it works: when you register, YOUR DEVICE creates a key pair. The private key never leaves the device and is unlocked
/// by your fingerprint (or Face ID / Windows Hello PIN). We only store the PUBLIC key. To sign in, the device signs a one-time
/// challenge; we check the signature with the stored public key. We never see or store a fingerprint.
///
/// The passkeys are "discoverable": the device itself knows which account they belong to, so signing in needs NO email.
/// Every challenge is single-use and expires after 3 minutes.
/// </summary>
public class PasskeyService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly WebAuthnOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<PasskeyService> _log;

    private record Pending(string Purpose, string? UserId, string OptionsJson, string RpId, string Origin, DateTime ExpiresUtc);
    private sealed class Counter { public int Failures; }

    public PasskeyService(ApplicationDbContext db, IMemoryCache cache, IOptions<WebAuthnOptions> options, IHostEnvironment env, ILogger<PasskeyService> log)
    {
        _db = db;
        _cache = cache;
        _options = options.Value;
        _env = env;
        _log = log;
    }

    // ───────────────────────── configuration ─────────────────────────

    /// <summary>
    /// Which site address the passkey belongs to (the "relying party"). It MUST equal the address in the browser.
    /// Development: taken from the page itself, so https://localhost:7180 and your tunnel link both work with no .env change.
    /// Anywhere else: ONLY the fixed PUBLIC_URL (never trust what a client says).
    /// </summary>
    private (string RpId, HashSet<string> Origins) Resolve(string? clientOrigin)
    {
        if (_env.IsDevelopment() && Uri.TryCreate(clientOrigin, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.IsLoopback))
            return (u.Host, [u.GetLeftPart(UriPartial.Authority)]);

        return (_options.RelyingPartyId, _options.Origins.ToHashSet());
    }

    private IFido2 CreateFido(string rpId, HashSet<string> origins) =>
        new Fido2(new Fido2Configuration { RPID = rpId, RPName = _options.RelyingPartyName, Origins = origins }, null!);

    // ───────────────────────── one-time options store ─────────────────────────

    private static string Key(string id) => "passkey-options:" + id;

    private string Remember(string purpose, string? userId, string optionsJson, string rpId, string origin)
    {
        var id = WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        _cache.Set(Key(id), new Pending(purpose, userId, optionsJson, rpId, origin, DateTime.UtcNow.AddMinutes(3)), TimeSpan.FromMinutes(3));
        return id;
    }

    /// <summary>One use only: removed as soon as it is read, even if the attempt then fails.</summary>
    private Pending? Take(string id, string purpose, string? userId)
    {
        if (string.IsNullOrEmpty(id) || !_cache.TryGetValue(Key(id), out Pending? p) || p is null) return null;
        _cache.Remove(Key(id));
        return p.Purpose == purpose && p.UserId == userId && p.ExpiresUtc > DateTime.UtcNow ? p : null;
    }

    // ───────────────────────── register (signed in) ─────────────────────────

    public async Task<PasskeyBegin> BeginRegistrationAsync(ApplicationUser user, PasskeyOptionsRequest request)
    {
        var (rpId, origins) = Resolve(request.Origin);
        var existing = await _db.BiometricCredentials.Where(c => c.UserId == user.Id && !c.IsRevoked).Select(c => c.CredentialId).ToListAsync();

        var options = CreateFido(rpId, origins).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = Encoding.UTF8.GetBytes(user.Id), Name = user.Email ?? user.UserName ?? user.Id, DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? "User") : user.FullName },
            ExcludeCredentials = existing.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),   // one passkey per device
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,                 // discoverable: sign in without typing the email
                UserVerification = UserVerificationRequirement.Required,       // fingerprint / face / PIN of the device is REQUIRED
                AuthenticatorAttachment = request.Attachment switch
                {
                    Statuses.AuthenticatorType.Platform => AuthenticatorAttachment.Platform,
                    Statuses.AuthenticatorType.CrossPlatform => AuthenticatorAttachment.CrossPlatform,
                    _ => null,
                },
            },
            AttestationPreference = AttestationConveyancePreference.None,       // we do not need to know the device brand
            Extensions = new AuthenticationExtensionsClientInputs { CredProps = true },
        });

        var id = Remember("register", user.Id, options.ToJson(), rpId, origins.First());
        return new PasskeyBegin(id, JsonDocument.Parse(options.ToJson()).RootElement.Clone());
    }

    public async Task<PasskeyResult> CompleteRegistrationAsync(ApplicationUser user, PasskeyRegisterRequest request, CancellationToken ct = default)
    {
        var pending = Take(request.OptionsId, "register", user.Id);
        if (pending is null) return new PasskeyResult(false, "This attempt expired. Please start again.");
        if (request.Credential is null) return new PasskeyResult(false, "The device sent no credential.");

        try
        {
            var options = CredentialCreateOptions.FromJson(pending.OptionsJson);
            var fido = CreateFido(pending.RpId, [pending.Origin]);

            var created = await fido.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = request.Credential,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = async (p, token) =>
                    !await _db.BiometricCredentials.AnyAsync(c => c.CredentialId == p.CredentialId, token),
            }, ct);

            var transports = created.Transports?.Select(t => t.ToString().ToLowerInvariant()).ToArray() ?? [];
            _db.BiometricCredentials.Add(new BiometricCredential
            {
                UserId = user.Id,
                CredentialId = created.Id,
                PublicKey = created.PublicKey,
                SignCount = created.SignCount,
                DeviceName = CleanName(request.DeviceName),
                AuthenticatorType = transports.Contains("internal") ? Statuses.AuthenticatorType.Platform : Statuses.AuthenticatorType.CrossPlatform,
                Transports = transports.Length == 0 ? null : string.Join(',', transports),
            });
            await _db.SaveChangesAsync(ct);

            var count = await _db.BiometricCredentials.CountAsync(c => c.UserId == user.Id && !c.IsRevoked, ct);
            return new PasskeyResult(true, "Fingerprint saved on this device. You can now sign in with it.", user.Id, count);
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Passkey registration refused: {Message}", ex.Message);
            return new PasskeyResult(false, "The device could not be registered. Please try again (open the site with the same address you will sign in with).");
        }
    }

    private static string CleanName(string? name)
    {
        var text = string.IsNullOrWhiteSpace(name) ? "My device" : name.Trim();
        return text.Length > 100 ? text[..100] : text;
    }

    // ───────────────────────── sign in (not signed in, no email needed) ─────────────────────────

    public PasskeyBegin BeginLogin(PasskeyOptionsRequest request)
    {
        var (rpId, origins) = Resolve(request.Origin);

        // Empty allow-list = "discoverable" sign-in: the device offers its passkeys and the account comes from the passkey itself.
        // So the server never reveals whether an email or an account exists.
        var options = CreateFido(rpId, origins).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });

        var id = Remember("login", null, options.ToJson(), rpId, origins.First());
        return new PasskeyBegin(id, JsonDocument.Parse(options.ToJson()).RootElement.Clone());
    }

    public async Task<PasskeyResult> CompleteLoginAsync(PasskeyLoginRequest request, CancellationToken ct = default)
    {
        const string notRecognised = "Fingerprint not recognised. Please try again, or sign in with your password.";

        var pending = Take(request.OptionsId, "login", null);
        if (pending is null) return new PasskeyResult(false, "This attempt expired. Please start again.");
        if (request.Credential?.RawId is null) return new PasskeyResult(false, notRecognised);

        var saved = await _db.BiometricCredentials.FirstOrDefaultAsync(c => c.CredentialId == request.Credential.RawId && !c.IsRevoked, ct);
        if (saved is null) return new PasskeyResult(false, notRecognised);

        try
        {
            var options = AssertionOptions.FromJson(pending.OptionsJson);
            var fido = CreateFido(pending.RpId, [pending.Origin]);

            var result = await fido.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.Credential,
                OriginalOptions = options,
                StoredPublicKey = saved.PublicKey,
                StoredSignatureCounter = (uint)saved.SignCount,
                // the passkey must belong to the account it claims (user handle = our user id)
                IsUserHandleOwnerOfCredentialIdCallback = (p, token) =>
                    Task.FromResult(p.UserHandle is { Length: > 0 } && Encoding.UTF8.GetString(p.UserHandle) == saved.UserId),
            }, ct);

            saved.SignCount = result.SignCount;     // a counter that goes backwards means a cloned key: the library refuses it
            saved.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new PasskeyResult(true, "ok", saved.UserId);
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Passkey sign-in refused: {Message}", ex.Message);
            return new PasskeyResult(false, notRecognised, saved.UserId);
        }
    }

    // ───────────────────────── approve one action (signed in, "step-up") ─────────────────────────
    // Used by the assistant's command center: before an action starts, the SIGNED-IN person proves with their fingerprint that it is
    // really them. Unlike sign-in, the challenge only accepts the signed-in user's OWN passkeys and is tied to ONE action id.

    /// <summary>Null when the user has no fingerprint device saved (they cannot approve with a fingerprint).</summary>
    public async Task<PasskeyBegin?> BeginApprovalAsync(ApplicationUser user, string actionId, PasskeyOptionsRequest request)
    {
        var (rpId, origins) = Resolve(request.Origin);
        var own = await _db.BiometricCredentials.Where(c => c.UserId == user.Id && !c.IsRevoked).Select(c => c.CredentialId).ToListAsync();
        if (own.Count == 0) return null;

        var options = CreateFido(rpId, origins).GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = own.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            UserVerification = UserVerificationRequirement.Required,          // the device must check the fingerprint / PIN again, now
        });

        var id = Remember("approve:" + actionId, user.Id, options.ToJson(), rpId, origins.First());
        return new PasskeyBegin(id, JsonDocument.Parse(options.ToJson()).RootElement.Clone());
    }

    public async Task<PasskeyResult> CompleteApprovalAsync(ApplicationUser user, string actionId, PasskeyLoginRequest request, CancellationToken ct = default)
    {
        const string refused = "The fingerprint check failed. Please try again.";

        var pending = Take(request.OptionsId, "approve:" + actionId, user.Id);       // one use, only for THIS user and THIS action
        if (pending is null) return new PasskeyResult(false, "This check expired. Please start again.");
        if (request.Credential?.RawId is null) return new PasskeyResult(false, refused, user.Id);

        // only the signed-in user's own, active passkeys count
        var saved = await _db.BiometricCredentials.FirstOrDefaultAsync(c => c.CredentialId == request.Credential.RawId && c.UserId == user.Id && !c.IsRevoked, ct);
        if (saved is null) return new PasskeyResult(false, refused, user.Id);

        try
        {
            var options = AssertionOptions.FromJson(pending.OptionsJson);
            var fido = CreateFido(pending.RpId, [pending.Origin]);

            var result = await fido.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = request.Credential,
                OriginalOptions = options,
                StoredPublicKey = saved.PublicKey,
                StoredSignatureCounter = (uint)saved.SignCount,
                // the credential was already looked up by user id above, so ownership is certain (not every device returns a user handle here)
                IsUserHandleOwnerOfCredentialIdCallback = (p, token) => Task.FromResult(saved.UserId == user.Id),
            }, ct);

            saved.SignCount = result.SignCount;
            saved.LastUsedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new PasskeyResult(true, "ok", user.Id);
        }
        catch (Exception ex)
        {
            _log.LogInformation(ex, "Passkey approval refused: {Message}", ex.Message);
            return new PasskeyResult(false, refused, user.Id);
        }
    }

    // ───────────────────────── devices ─────────────────────────

    public async Task<List<PasskeyDevice>> ListAsync(string userId) =>
        await _db.BiometricCredentials.AsNoTracking().Where(c => c.UserId == userId && !c.IsRevoked)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new PasskeyDevice(c.Id, c.DeviceName, c.AuthenticatorType, c.CreatedAt, c.LastUsedAt)).ToListAsync();

    public async Task<bool> RemoveAsync(string userId, int id)
    {
        var item = await _db.BiometricCredentials.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && !c.IsRevoked);
        if (item is null) return false;
        item.IsRevoked = true;
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<int> CountAsync(string userId) => _db.BiometricCredentials.CountAsync(c => c.UserId == userId && !c.IsRevoked);

    // ───────────────────────── throttle (sign-in has no account to lock before the passkey is found) ─────────────────────────

    private static string ThrottleKey(string client) => "passkey-fail:" + client;

    public bool IsThrottled(string client) =>
        _cache.TryGetValue(ThrottleKey(client), out Counter? c) && c is not null && c.Failures >= _options.MaxFailuresPer10Minutes;

    public void RegisterFailure(string client)
    {
        var counter = _cache.GetOrCreate(ThrottleKey(client), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return new Counter();
        })!;
        Interlocked.Increment(ref counter.Failures);
    }

    public void ClearFailures(string client) => _cache.Remove(ThrottleKey(client));
}
