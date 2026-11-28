using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TenderSystem.Data;
using TenderSystem.Data.Entities.Biometrics;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Biometrics;

// ───────────── what the browser sends / receives ─────────────
public class FaceChallengeRequest
{
    public string? Email { get; set; }
    /// <summary>true = ask for the head-turn check even if the one-step sign-in is available (used right after "not sure").</summary>
    public bool Strict { get; set; }
}
public class FaceFrameDto { public string Step { get; set; } = ""; public string Image { get; set; } = ""; }
public class FaceSubmitRequest
{
    public string ChallengeId { get; set; } = "";
    public string? Email { get; set; }
    public List<FaceFrameDto> Frames { get; set; } = [];
}
public record FaceChallenge(string ChallengeId, string[] Steps, int ExpiresInSeconds);
/// <param name="NeedsTurn">one-step sign-in only: the face looks fine but the anti-photo check was not sure, so ask again WITH a head turn (not a failure by itself)</param>
public record FaceAnalysis(bool Ok, string Reason, float[]? Embedding, double Liveness, bool NeedsTurn = false);
public record FaceMatch(bool Ok, double Similarity);
/// <summary>One saved face as the profile page shows it. Only when it was added: the template itself never leaves FaceService.</summary>
public record FaceProfile(int Id, DateTime CreatedAt);
public class FaceRemoveRequest { public int Id { get; set; } }
public record FaceIdentity(bool Ok, string? UserId, double Best, double Second);

/// <summary>
/// Everything about face login, in four jobs:
///   CreateChallenge  - a one-time "do these moves" request (2 minutes, usable once, tied to a person)
///   AnalyzeAsync     - check the frames really show ONE live person doing the moves, and compute the face embedding
///   EnrollAsync      - save the encrypted template for a signed-in user
///   MatchAsync       - compare a live embedding with the user's saved templates
/// The controllers only pass data in and out. No picture is ever stored: frames live in memory for the request only.
/// </summary>
public class FaceService
{
    private const int MaxFrames = 30, MinFrames = 6, MinFramesOneStep = 4, MaxFrameBytes = 2_000_000, MaxTemplatesPerUser = 3;

    private readonly FaceEngine _engine;
    private readonly FaceTemplateProtector _protector;
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly FaceOptions _options;
    private readonly ILogger<FaceService> _log;

    private sealed class Counter { public int Failures; }
    private record Pending(string Purpose, string Subject, DateTime ExpiresUtc, string[] Steps, bool Passive);

    public FaceService(FaceEngine engine, FaceTemplateProtector protector, ApplicationDbContext db, IMemoryCache cache,
        IOptions<FaceOptions> options, ILogger<FaceService> log)
    {
        _engine = engine;
        _protector = protector;
        _db = db;
        _cache = cache;
        _options = options.Value;
        _log = log;
    }

    // ───────────────────────── challenge ─────────────────────────

    /// <summary>True when the one-step sign-in can be offered: switched on, and the anti-photo model files are there.</summary>
    public bool PassiveReady => _options.PassiveLiveness && _engine.Passive.Available;

    /// <param name="purpose">"enroll", "login" or "approve"</param>
    /// <param name="subject">enroll: the user id. login: the lower-cased email the user typed. approve: the Admin and the action.</param>
    /// <param name="strict">login and approve: skip the one-step check and ask for the head turn (after "not sure", or for an address with many failures)</param>
    public FaceChallenge CreateChallenge(string purpose, string subject, bool strict = false)
    {
        // Sign-in is ONE step by default: just look straight, and the anti-photo model checks the face is live (see PassiveLivenessModel).
        // When that is off, missing, or not sure, the challenge is "center" first, then a RANDOM side, so a prepared video cannot know which side
        // comes (FACE_LOGIN_TURNS=1). Enrollment always asks for both sides: it saves a template.
        // Approvals (the assistant's command center) use the same adaptive flow as sign-in: they are 1:1 against the signed-in Admin's own face.
        var adaptive = purpose is "login" or "approve";
        var passive = adaptive && !strict && PassiveReady;

        var sides = Random.Shared.Next(2) == 0
            ? new[] { FaceLiveness.Left, FaceLiveness.Right }
            : new[] { FaceLiveness.Right, FaceLiveness.Left };

        var steps = passive
            ? new[] { FaceLiveness.Center }
            : adaptive && _options.LoginTurns <= 1
                ? new[] { FaceLiveness.Center, sides[0] }
                : new[] { FaceLiveness.Center, sides[0], sides[1] };

        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        _cache.Set(CacheKey(id), new Pending(purpose, subject, DateTime.UtcNow.AddMinutes(2), steps, passive), TimeSpan.FromMinutes(2));
        return new FaceChallenge(id, steps, 120);
    }

    private static string CacheKey(string id) => "face-challenge:" + id;

    /// <summary>One try per challenge: it is removed as soon as it is read, even if the attempt then fails.</summary>
    private Pending? TakeChallenge(string id, string purpose, string subject)
    {
        if (string.IsNullOrEmpty(id) || !_cache.TryGetValue(CacheKey(id), out Pending? p) || p is null) return null;
        _cache.Remove(CacheKey(id));
        return p.Purpose == purpose && p.Subject == subject && p.ExpiresUtc > DateTime.UtcNow ? p : null;
    }

    // ───────────────────────── analyse the frames ─────────────────────────

    public async Task<FaceAnalysis> AnalyzeAsync(string challengeId, string purpose, string subject, IReadOnlyList<FaceFrameDto> frames)
    {
        var challenge = TakeChallenge(challengeId, purpose, subject);
        if (challenge is null)
            return Fail("This attempt expired. Please start again.", "challenge invalid or expired");
        // the one-step sign-in has a single straight step, so it needs fewer frames than the moving ones
        if (frames.Count < (challenge.Passive ? MinFramesOneStep : MinFrames) || frames.Count > MaxFrames)
            return Fail("The camera did not capture enough images. Please try again.", $"frame count {frames.Count}");

        // heavy CPU work: keep it off the request thread
        return await Task.Run(() => Analyze(frames, challenge.Steps, challenge.Passive));
    }

    private FaceAnalysis Analyze(IReadOnlyList<FaceFrameDto> frames, string[] steps, bool passive)
    {
        var results = new List<(string Step, FaceFrameResult Frame)>();
        foreach (var f in frames)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(f.Image); }
            catch (FormatException) { return Fail("Could not read the camera images.", "bad base64"); }
            if (bytes.Length is 0 or > MaxFrameBytes) return Fail("Could not read the camera images.", "frame size");

            // embeddings are computed for every readable frame (needed for the identity-consistency check below)
            results.Add((f.Step, _engine.Analyze(bytes, computeEmbedding: true, computePassive: passive)));
        }

        // Numbers you need for tuning (FACE_YAW_*): the measured head turn of every frame, per step. "x" = no clear face in that frame.
        _log.LogInformation("Face frames (head turn per step; straight ~0, turned should reach +/-{TurnMin}): {Summary}",
            _options.YawTurnMin,
            string.Join(" | ", results.GroupBy(r => r.Step).Select(g =>
                $"{g.Key}: " + string.Join(" ", g.Select(r => r.Frame.HasFace ? r.Frame.Yaw.ToString("0.00") : "x")))));

        var liveness = FaceLiveness.Evaluate(results, steps, _options);
        _log.LogInformation("Face liveness: pass={Pass} score={Score:0.00} ({Reason})", liveness.Pass, liveness.Score, liveness.Reason);
        if (!liveness.Pass) return new FaceAnalysis(false, liveness.Reason, null, liveness.Score);

        // Identity consistency: it must be the SAME face in the straight frames, and in the turned frames too
        // (stops someone swapping to a different person in the middle of the movement).
        var centerEmbeddings = results
            .Where(r => r.Step == FaceLiveness.Center && r.Frame is { HasFace: true, Embedding: not null }
                        && Math.Abs(r.Frame.Yaw) <= _options.YawCenterMax)
            .Select(r => r.Frame.Embedding!).ToList();
        if (centerEmbeddings.Count < 3) return Fail("Look straight at the camera and keep your face inside the frame.", "too few straight frames");

        var mean = FaceEngine.Mean(centerEmbeddings);
        if (centerEmbeddings.Any(e => FaceEngine.Similarity(e, mean) < 0.6))
            return Fail("The face changed during the capture. Please try again.", "center frames inconsistent");

        var turned = results.Where(r => r.Step != FaceLiveness.Center && r.Frame.Embedding is not null).Select(r => r.Frame.Embedding!).ToList();
        if (turned.Count > 0 && turned.Average(e => FaceEngine.Similarity(e, mean)) < 0.25)
            return Fail("The face changed during the capture. Please try again.", "turned frames differ from straight frames");

        // One-step sign-in: nobody moved, so the only proof of a live face is the anti-photo model. It grants the shortcut only when it is
        // clearly sure (high average AND most straight frames high). Otherwise: not a failure, just "ask again, this time with a head turn".
        if (passive)
        {
            var reals = results
                .Where(r => r.Step == FaceLiveness.Center && r.Frame is { HasFace: true, RealProbability: not null } && Math.Abs(r.Frame.Yaw) <= _options.YawCenterMax)
                .Select(r => r.Frame.RealProbability!.Value).ToList();
            var needed = _options.PassiveMinReal;
            var average = reals.Count > 0 ? reals.Average() : 0;
            var share = reals.Count > 0 ? reals.Count(v => v >= needed) / (double)reals.Count : 0;
            var sure = _engine.Passive.Available && reals.Count >= 3 && average >= needed && share >= 0.75;
            _log.LogInformation("Passive liveness: average real={Average:0.00} (needed {Needed:0.00}), {Share:P0} of {Count} straight frames above it => {Result}",
                average, needed, share, reals.Count, sure ? "one step is enough" : "ask for a head turn");
            if (!sure)
                return new FaceAnalysis(false, "One more move, please: turn your head to the side when asked.", null, liveness.Score, NeedsTurn: true);
        }

        return new FaceAnalysis(true, "ok", mean, liveness.Score);
    }

    private FaceAnalysis Fail(string message, string logReason)
    {
        _log.LogInformation("Face analysis failed: {Reason}", logReason);
        return new FaceAnalysis(false, message, null, 0);
    }

    // ───────────────────────── enroll / match ─────────────────────────

    /// <summary>Saves a new encrypted template. A user keeps at most 3 (one per device is typical); the oldest is retired.</summary>
    public async Task<int> EnrollAsync(string userId, float[] embedding, double liveness)
    {
        var active = await _db.FaceEnrollments.Where(e => e.UserId == userId && e.IsActive).OrderBy(e => e.CreatedAt).ToListAsync();
        while (active.Count >= MaxTemplatesPerUser)
        {
            active[0].IsActive = false;
            active[0].UpdatedAt = DateTime.UtcNow;
            active.RemoveAt(0);
        }

        _db.FaceEnrollments.Add(new FaceEnrollment
        {
            UserId = userId,
            FaceTemplate = _protector.Protect(embedding),
            ModelVersion = FaceEngine.ModelVersion,
            LivenessScore = (decimal)Math.Round(liveness, 4),
        });
        await _db.SaveChangesAsync();
        return active.Count + 1;
    }

    public Task<int> CountAsync(string userId) => _db.FaceEnrollments.CountAsync(e => e.UserId == userId && e.IsActive);

    /// <summary>The saved faces of ONE person, oldest first (retired ones are not shown).</summary>
    public Task<List<FaceProfile>> ListAsync(string userId) =>
        _db.FaceEnrollments.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).OrderBy(e => e.CreatedAt)
            .Select(e => new FaceProfile(e.Id, e.CreatedAt)).ToListAsync();

    /// <summary>
    /// Forgets one of MY saved faces for good: the encrypted template row is deleted, not just switched off.
    /// False when the face does not exist, is already retired, or belongs to someone else.
    /// </summary>
    public async Task<bool> RemoveAsync(string userId, int id)
    {
        var row = await _db.FaceEnrollments.FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.IsActive);
        if (row is null) return false;
        _db.FaceEnrollments.Remove(row);
        await _db.SaveChangesAsync();
        return true;
    }

    public bool AllowIdentify => _options.AllowIdentify;

    /// <summary>
    /// "Smart" sign-in: who is this? Searches ALL enrolled people (best template of each person) and only names someone when
    ///   1) the best similarity reaches FACE_IDENTIFY_THRESHOLD and
    ///   2) it beats the second-best PERSON by FACE_IDENTIFY_MARGIN (look-alikes or two accounts with the same face are refused).
    /// Wider search means a higher chance of a wrong match than the 1:1 check, so the bar is higher and failures are throttled.
    /// </summary>
    public async Task<FaceIdentity> IdentifyAsync(float[] liveEmbedding)
    {
        var rows = await _db.FaceEnrollments.AsNoTracking()
            .Where(e => e.IsActive && e.ModelVersion == FaceEngine.ModelVersion)
            .Select(e => new { e.UserId, e.FaceTemplate }).ToListAsync();

        var bestPerUser = new Dictionary<string, double>();
        foreach (var row in rows)
        {
            try
            {
                var sim = FaceEngine.Similarity(liveEmbedding, _protector.Unprotect(row.FaceTemplate));
                if (!bestPerUser.TryGetValue(row.UserId, out var current) || sim > current) bestPerUser[row.UserId] = sim;
            }
            catch (CryptographicException) { _log.LogWarning("A face template of user {UserId} could not be decrypted (wrong key?).", row.UserId); }
        }

        var ranked = bestPerUser.OrderByDescending(kv => kv.Value).ToList();
        if (ranked.Count == 0) return new FaceIdentity(false, null, -1, -1);

        var best = ranked[0].Value;
        var second = ranked.Count > 1 ? ranked[1].Value : -1;
        var ok = best >= _options.IdentifyThreshold && best - second >= _options.IdentifyMargin;
        return new FaceIdentity(ok, ok ? ranked[0].Key : null, best, second);
    }

    // ───────────────────────── throttle for "smart" attempts ─────────────────────────
    // Without an email there is no account to lock, so failures are counted per client address (10 minutes window).

    private static string ThrottleKey(string client) => "face-identify-fail:" + client;

    public bool IsThrottled(string client) =>
        _cache.TryGetValue(ThrottleKey(client), out Counter? c) && c is not null && c.Failures >= _options.IdentifyMaxFailures;

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

    /// <summary>Best similarity against the user's saved templates; Ok when it reaches FACE_MATCH_THRESHOLD.</summary>
    public async Task<FaceMatch> MatchAsync(string userId, float[] liveEmbedding)
    {
        var templates = await _db.FaceEnrollments.AsNoTracking()
            .Where(e => e.UserId == userId && e.IsActive && e.ModelVersion == FaceEngine.ModelVersion)
            .Select(e => e.FaceTemplate).ToListAsync();

        double best = -1;
        foreach (var blob in templates)
        {
            try { best = Math.Max(best, FaceEngine.Similarity(liveEmbedding, _protector.Unprotect(blob))); }
            catch (CryptographicException) { _log.LogWarning("A face template of user {UserId} could not be decrypted (wrong key?).", userId); }
        }
        return new FaceMatch(best >= _options.MatchThreshold, best);
    }
}
