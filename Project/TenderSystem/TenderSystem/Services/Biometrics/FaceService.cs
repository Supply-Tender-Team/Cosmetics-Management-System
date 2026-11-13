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
public class FaceChallengeRequest { public string? Email { get; set; } }
public class FaceFrameDto { public string Step { get; set; } = ""; public string Image { get; set; } = ""; }
public class FaceSubmitRequest
{
    public string ChallengeId { get; set; } = "";
    public string? Email { get; set; }
    public List<FaceFrameDto> Frames { get; set; } = [];
}
public record FaceChallenge(string ChallengeId, string[] Steps, int ExpiresInSeconds);
public record FaceAnalysis(bool Ok, string Reason, float[]? Embedding, double Liveness);
public record FaceMatch(bool Ok, double Similarity);

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
    private const int MaxFrames = 30, MinFrames = 6, MaxFrameBytes = 2_000_000, MaxTemplatesPerUser = 3;

    private readonly FaceEngine _engine;
    private readonly FaceTemplateProtector _protector;
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly FaceOptions _options;
    private readonly ILogger<FaceService> _log;

    private record Pending(string Purpose, string Subject, DateTime ExpiresUtc);

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

    /// <param name="purpose">"enroll" or "login"</param>
    /// <param name="subject">enroll: the user id. login: the lower-cased email the user typed.</param>
    public FaceChallenge CreateChallenge(string purpose, string subject)
    {
        // Always "center" first, then the two turns in random order, so a prepared video cannot be replayed blindly.
        var turns = Random.Shared.Next(2) == 0
            ? new[] { FaceLiveness.Left, FaceLiveness.Right }
            : new[] { FaceLiveness.Right, FaceLiveness.Left };

        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        _cache.Set(CacheKey(id), new Pending(purpose, subject, DateTime.UtcNow.AddMinutes(2)), TimeSpan.FromMinutes(2));
        return new FaceChallenge(id, [FaceLiveness.Center, turns[0], turns[1]], 120);
    }

    private static string CacheKey(string id) => "face-challenge:" + id;

    /// <summary>One try per challenge: it is removed as soon as it is read, even if the attempt then fails.</summary>
    private bool TakeChallenge(string id, string purpose, string subject)
    {
        if (string.IsNullOrEmpty(id) || !_cache.TryGetValue(CacheKey(id), out Pending? p) || p is null) return false;
        _cache.Remove(CacheKey(id));
        return p.Purpose == purpose && p.Subject == subject && p.ExpiresUtc > DateTime.UtcNow;
    }

    // ───────────────────────── analyse the frames ─────────────────────────

    public async Task<FaceAnalysis> AnalyzeAsync(string challengeId, string purpose, string subject, IReadOnlyList<FaceFrameDto> frames)
    {
        if (!TakeChallenge(challengeId, purpose, subject))
            return Fail("This attempt expired. Please start again.", "challenge invalid or expired");
        if (frames.Count < MinFrames || frames.Count > MaxFrames)
            return Fail("The camera did not capture enough images. Please try again.", $"frame count {frames.Count}");

        // heavy CPU work: keep it off the request thread
        return await Task.Run(() => Analyze(frames));
    }

    private FaceAnalysis Analyze(IReadOnlyList<FaceFrameDto> frames)
    {
        var results = new List<(string Step, FaceFrameResult Frame)>();
        foreach (var f in frames)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(f.Image); }
            catch (FormatException) { return Fail("Could not read the camera images.", "bad base64"); }
            if (bytes.Length is 0 or > MaxFrameBytes) return Fail("Could not read the camera images.", "frame size");

            // embeddings are computed for every readable frame (needed for the identity-consistency check below)
            results.Add((f.Step, _engine.Analyze(bytes, computeEmbedding: true)));
        }

        // Numbers you need for tuning (FACE_YAW_*): the measured head turn of every frame, per step. "x" = no clear face in that frame.
        _log.LogInformation("Face frames (head turn per step; straight ~0, turned should reach +/-{TurnMin}): {Summary}",
            _options.YawTurnMin,
            string.Join(" | ", results.GroupBy(r => r.Step).Select(g =>
                $"{g.Key}: " + string.Join(" ", g.Select(r => r.Frame.HasFace ? r.Frame.Yaw.ToString("0.00") : "x")))));

        var liveness = FaceLiveness.Evaluate(results, _options);
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
