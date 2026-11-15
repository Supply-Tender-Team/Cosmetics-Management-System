using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TenderSystem.Services.Biometrics;

/// <summary>
/// "Is this a live face, or a photo / a phone screen?" from ONE straight camera frame (passive liveness, no head movement needed).
/// Two small open-source models (Silent-Face-Anti-Spoofing, MiniFASNetV2 and MiniFASNetV1SE, Apache-2.0, ONNX files in
/// Services/Biometrics/Models, see NOTICE-anti-spoofing.txt there). Each looks at a crop around the face at its own scale
/// (2.7 and 4.0 times the face box), 80 x 80 pixels, and answers three numbers: [fake, real, fake]. We average the "real" part of both.
///
/// Honest limit: this is a statistical check, not a certified one. A very good screen replay can fool it, and a poor webcam or dim light can
/// make a real person look doubtful. That is why it only GRANTS the one-step sign-in; when it is not sure the login falls back to the head-turn check.
/// If the model files are missing, <see cref="Available"/> is false and face sign-in simply keeps asking for the head turn.
/// </summary>
public sealed class PassiveLivenessModel : IDisposable
{
    private const int Size = 80;
    private readonly InferenceSession? _v2;       // MiniFASNetV2, crop scale 2.7
    private readonly InferenceSession? _v1se;     // MiniFASNetV1SE, crop scale 4.0
    private readonly string? _v2Input, _v1seInput;

    public bool Available => _v2 is not null && _v1se is not null;
    public string? LoadProblem { get; }

    public PassiveLivenessModel(string? folder = null)
    {
        folder ??= Path.Combine(AppContext.BaseDirectory, "Services", "Biometrics", "Models");
        try
        {
            var v2 = Path.Combine(folder, "MiniFASNetV2.onnx");
            var v1 = Path.Combine(folder, "MiniFASNetV1SE.onnx");
            if (!File.Exists(v2) || !File.Exists(v1)) { LoadProblem = "model files not found in " + folder; return; }
            var options = new Microsoft.ML.OnnxRuntime.SessionOptions();
            _v2 = new InferenceSession(v2, options);
            _v1se = new InferenceSession(v1, options);
            _v2Input = _v2.InputMetadata.Keys.First();
            _v1seInput = _v1se.InputMetadata.Keys.First();
        }
        catch (Exception ex)
        {
            LoadProblem = ex.Message;
            _v2?.Dispose(); _v1se?.Dispose();
            _v2 = null; _v1se = null;
        }
    }

    /// <summary>Probability (0 to 1) that the face in this frame is real. Null when the model is not available or the box is unusable.</summary>
    public double? RealProbability(Image<Rgb24> image, float boxLeft, float boxTop, float boxWidth, float boxHeight)
    {
        if (!Available) return null;
        if (boxWidth < 8 || boxHeight < 8) return null;

        var a = Run(_v2!, _v2Input!, image, boxLeft, boxTop, boxWidth, boxHeight, 2.7f);
        var b = Run(_v1se!, _v1seInput!, image, boxLeft, boxTop, boxWidth, boxHeight, 4.0f);
        if (a is null || b is null) return null;
        return (a.Value + b.Value) / 2.0;
    }

    private static double? Run(InferenceSession session, string inputName, Image<Rgb24> image, float left, float top, float width, float height, float scale)
    {
        var tensor = Preprocess(image, (int)left, (int)top, (int)width, (int)height, scale);
        if (tensor is null) return null;
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var logits = results.First().AsEnumerable<float>().ToArray();
        if (logits.Length < 2) return null;

        var max = logits.Max();
        var exps = logits.Select(l => Math.Exp(l - max)).ToArray();
        return exps[1] / exps.Sum();          // index 1 = "real"
    }

    /// <summary>
    /// The same preprocessing the models were trained with: crop around the face, plain bilinear resize to 80 x 80
    /// (no smoothing filter, it would wipe out the fine texture the models look for), raw 0 to 255 values, channels in BGR order.
    /// </summary>
    private static DenseTensor<float>? Preprocess(Image<Rgb24> image, int x, int y, int boxW, int boxH, float scaleLimit)
    {
        int srcW = image.Width, srcH = image.Height;
        if (boxW <= 0 || boxH <= 0) return null;

        var scale = Math.Min(Math.Min((srcH - 1) / (float)boxH, (srcW - 1) / (float)boxW), scaleLimit);
        var newW = boxW * scale;
        var newH = boxH * scale;
        var cx = x + boxW / 2f;
        var cy = y + boxH / 2f;

        var x1 = Math.Max(0, (int)(cx - newW / 2));
        var y1 = Math.Max(0, (int)(cy - newH / 2));
        var x2 = Math.Min(srcW - 1, (int)(cx + newW / 2));
        var y2 = Math.Min(srcH - 1, (int)(cy + newH / 2));
        var cw = x2 - x1 + 1;
        var ch = y2 - y1 + 1;
        if (cw < 2 || ch < 2) return null;

        var tensor = new DenseTensor<float>([1, 3, Size, Size]);
        var sx = cw / (float)Size;
        var sy = ch / (float)Size;

        image.ProcessPixelRows(rows =>
        {
            for (var oy = 0; oy < Size; oy++)
            {
                var fy = (oy + 0.5f) * sy - 0.5f;
                var iy = (int)MathF.Floor(fy);
                var wy = fy - iy;
                var ya = Math.Clamp(iy, 0, ch - 1) + y1;
                var yb = Math.Clamp(iy + 1, 0, ch - 1) + y1;
                var rowA = rows.GetRowSpan(ya);
                var rowB = rows.GetRowSpan(yb);

                for (var ox = 0; ox < Size; ox++)
                {
                    var fx = (ox + 0.5f) * sx - 0.5f;
                    var ix = (int)MathF.Floor(fx);
                    var wx = fx - ix;
                    var xa = Math.Clamp(ix, 0, cw - 1) + x1;
                    var xb = Math.Clamp(ix + 1, 0, cw - 1) + x1;

                    var p00 = rowA[xa]; var p01 = rowA[xb]; var p10 = rowB[xa]; var p11 = rowB[xb];
                    float Lerp(float c00, float c01, float c10, float c11) =>
                        MathF.Round((c00 * (1 - wx) + c01 * wx) * (1 - wy) + (c10 * (1 - wx) + c11 * wx) * wy);   // whole numbers, like the 8-bit image the models were trained on

                    tensor[0, 0, oy, ox] = Lerp(p00.B, p01.B, p10.B, p11.B);   // blue first: the models expect BGR
                    tensor[0, 1, oy, ox] = Lerp(p00.G, p01.G, p10.G, p11.G);
                    tensor[0, 2, oy, ox] = Lerp(p00.R, p01.R, p10.R, p11.R);
                }
            }
        });
        return tensor;
    }

    public void Dispose()
    {
        _v2?.Dispose();
        _v1se?.Dispose();
    }
}
