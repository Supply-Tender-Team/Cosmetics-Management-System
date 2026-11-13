using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Biometrics;

public record LivenessResult(bool Pass, double Score, string Reason);

/// <summary>
/// The "is this a real person moving?" check. Pure maths on the frame results, no camera, no models.
///
/// The user is asked for three steps in random order of the turns: look straight, turn to one side, turn to the other.
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

    public static LivenessResult Evaluate(IReadOnlyList<(string Step, FaceFrameResult Frame)> frames, FaceOptions o)
    {
        var yawsByStep = new Dictionary<string, List<double>> { [Center] = [], [Left] = [], [Right] = [] };
        var good = 0;

        foreach (var (step, frame) in frames)
        {
            if (!yawsByStep.ContainsKey(step)) continue;                       // unknown step label: counts as a bad frame
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
        if (yawsByStep[Left].Count < 2 || yawsByStep[Right].Count < 2)
            return new(false, score, "Turn your head clearly to both sides when asked.");
        if (Math.Sign(yawsByStep[Left].Average()) == Math.Sign(yawsByStep[Right].Average()))
            return new(false, score, "Turn to one side first, then to the other side.");
        if (score < o.MinLivenessScore)
            return new(false, score, "The movement was not clear enough. Try again, slowly.");

        return new(true, score, "ok");
    }
}
