using FaceAiSharp;
using FaceAiSharp.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TenderSystem.Services.Biometrics;

/// <summary>What we learned from ONE camera frame.</summary>
/// <param name="HasFace">exactly one clear face was found</param>
/// <param name="Problem">why not, when HasFace is false (for logs only)</param>
/// <param name="Yaw">how far the head is turned: 0 = straight, about +/-0.3 or more = clearly turned (sign = side)</param>
/// <param name="WidthRatio">face width divided by image width (too small = too far from the camera)</param>
/// <param name="Embedding">the face "fingerprint" (list of numbers), length-normalised; null if not computed</param>
/// <param name="RealProbability">0 to 1: how likely this frame shows a live face and not a photo or a screen; null if not computed</param>
public record FaceFrameResult(bool HasFace, string? Problem, double Yaw, double WidthRatio, float[]? Embedding, double? RealProbability = null);

/// <summary>
/// Thin wrapper around the FaceAiSharp models (face detector with 5 landmarks + ArcFace embeddings).
/// Registered as a singleton: loading the models takes a second, so it is done once.
/// The models run on the CPU and ship inside the NuGet package (no download, no GPU).
/// </summary>
public sealed class FaceEngine : IDisposable
{
    public const string ModelVersion = "faceaisharp-bundle-0.6-arcface";

    private readonly IFaceDetectorWithLandmarks _detector;
    private readonly IFaceEmbeddingsGenerator _embedder;
    private readonly object _lock = new();   // the ONNX wrappers are not guaranteed thread-safe: one frame at a time

    /// <summary>The "photo or live face?" check (one-step sign-in). Not available when its model files are missing.</summary>
    public PassiveLivenessModel Passive { get; }

    public FaceEngine()
    {
        var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
        _detector = FaceAiSharpBundleFactory.CreateFaceDetectorWithLandmarks(options);
        _embedder = FaceAiSharpBundleFactory.CreateFaceEmbeddingsGenerator(options);
        Passive = new PassiveLivenessModel();
    }

    /// <summary>Decodes a JPEG, finds the face, estimates the head turn and (optionally) computes the embedding and the live-or-photo probability.</summary>
    public FaceFrameResult Analyze(byte[] jpegBytes, bool computeEmbedding, double minConfidence = 0.5, bool computePassive = false)
    {
        Image<Rgb24> image;
        try { image = Image.Load<Rgb24>(jpegBytes); }
        catch (Exception) { return new FaceFrameResult(false, "unreadable image", 0, 0, null); }

        using (image)
        {
            lock (_lock)
            {
                var faces = _detector.DetectFaces(image);
                if (faces.Count == 0) return new FaceFrameResult(false, "no face", 0, 0, null);
                if (faces.Count > 1) return new FaceFrameResult(false, "more than one face", 0, 0, null);

                var face = faces.First();
                if (face.Confidence is < 0 || (face.Confidence ?? 1f) < minConfidence)
                    return new FaceFrameResult(false, "low confidence", 0, 0, null);
                if (face.Landmarks is null || face.Landmarks.Count < 5)
                    return new FaceFrameResult(false, "no landmarks", 0, 0, null);

                // landmarks: 0 and 1 = the two eyes, 2 = nose tip, 3 and 4 = mouth corners
                var eyeA = face.Landmarks[0];
                var eyeB = face.Landmarks[1];
                var nose = face.Landmarks[2];
                var eyeDistance = Math.Abs(eyeB.X - eyeA.X);
                if (eyeDistance < 1) return new FaceFrameResult(false, "eyes too close together", 0, 0, null);

                // The nose sits in the middle between the eyes when the head is straight and slides sideways when it turns.
                var yaw = (nose.X - (eyeA.X + eyeB.X) / 2.0) / eyeDistance;
                var widthRatio = face.Box.Width / (double)image.Width;

                float[]? embedding = null;
                if (computeEmbedding)
                {
                    using var aligned = image.Clone();
                    _embedder.AlignFaceUsingLandmarks(aligned, face.Landmarks);
                    embedding = Normalize(_embedder.GenerateEmbedding(aligned));
                }

                double? real = null;
                if (computePassive && Passive.Available)
                    real = Passive.RealProbability(image, face.Box.X, face.Box.Y, face.Box.Width, face.Box.Height);

                return new FaceFrameResult(true, null, yaw, widthRatio, embedding, real);
            }
        }
    }

    public static float[] Normalize(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm < 1e-6f ? v : v.Select(x => x / norm).ToArray();
    }

    /// <summary>Average of several embeddings, normalised again (more stable than a single frame).</summary>
    public static float[] Mean(IReadOnlyList<float[]> embeddings)
    {
        var mean = new float[embeddings[0].Length];
        foreach (var e in embeddings)
            for (var i = 0; i < mean.Length; i++) mean[i] += e[i];
        for (var i = 0; i < mean.Length; i++) mean[i] /= embeddings.Count;
        return Normalize(mean);
    }

    /// <summary>1 = identical face, around 0 = different people. Both vectors must be normalised.</summary>
    public static double Similarity(float[] a, float[] b) => a.Length == b.Length ? GeometryExtensions.CosineSimilarity(a, b) : -1;

    public void Dispose()
    {
        (_detector as IDisposable)?.Dispose();
        (_embedder as IDisposable)?.Dispose();
        Passive.Dispose();
    }
}
