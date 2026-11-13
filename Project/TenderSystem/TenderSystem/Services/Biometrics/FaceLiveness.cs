using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Biometrics;

public record LivenessResult(bool Pass, double Score, string Reason);

/// <summary>
/// The "is this a real person moving?" check. Pure maths on the frame results, no camera, no models.
///
/// The user is asked for: look straight, then turn to one side (login) or to both sides (enrollment); the side(s) are random.
/// A photo or a screenshot cannot turn its head, so it fails. We require:
///   - enough frames where the head is straight during the "center" step,
///   - enough frames where the head is clearly turned during each turn step,
///   - the two turns go to OPPOSITE sides,
///   - most of all frames behaved as asked (Score >= FACE_MIN_LIVENESS_SCORE).
/// Honest limit: a replayed VIDEO of the real person turning could pass. This is a challenge-response check, not a certified one.
/// </summary>
public static class FaceLiveness
{
    public const string Center = "center";
    public const string Left = "left";
    public const string Right = "right";

    /// <summary>All three steps (used by enrollment).</summary>
    public static LivenessResult Evaluate(IReadOnlyList<(string Step, FaceFrameResult Frame)> frames, FaceOptions o) =>
        Evaluate(frames, [Center, Left, Right], o);

    /// <param name="expectedSteps">exactly the steps the server asked for in this challenge, e.g. [center, left] at login.
    /// Frames labelled with any other step are refused: an attacker cannot send "both sides" to cover a random question.</param>
    public static LivenessResult Evaluate(IReadOnlyList<(string Step, FaceFrameResult Frame)> frames, string[] expectedSteps, FaceOptions o)
    {
        var expected = expectedSteps.ToHashSet();
        var yawsByStep = expectedSteps.ToDictionary(s => s, _ => new List<double>());
        var good = 0;

        foreach (var (step, frame) in frames)
        {
            if (!expected.Contains(step))
                return new(false, 0, "The camera sent unexpected data. Please try again.");
            if (!frame.HasFace || frame.WidthRatio < o.MinFaceWidthRatio) continue;

            var behavedAsAsked = step == Center
                ? Math.Abs(frame.Yaw) <= o.YawCenterMax
                : Math.Abs(frame.Yaw) >= o.YawTurnMin;

            if (!behavedAsAsked) continue;
            good++;
            yawsByStep[step].Add(frame.Yaw);
        }

        var score = frames.Count == 0 ? 0 : (double)good / frames.Count;

        if (yawsByStep[Center].Count < 3)
            return new(false, score, "Look straight at the camera and keep your face inside the frame.");

        var turns = expectedSteps.Where(s => s != Center).ToList();
        if (turns.Any(t => yawsByStep[t].Count < 2))
            return new(false, score, turns.Count == 1 ? "Turn your head clearly to the side when asked." : "Turn your head clearly to both sides when asked.");

        // two turns must go to OPPOSITE sides (with one turn there is nothing to compare)
        if (turns.Count == 2 && Math.Sign(yawsByStep[turns[0]].Average()) == Math.Sign(yawsByStep[turns[1]].Average()))
            return new(false, score, "Turn to one side first, then to the other side.");

        if (score < o.MinLivenessScore)
            return new(false, score, "The movement was not clear enough. Try again, slowly.");

        return new(true, score, "ok");
    }
}
