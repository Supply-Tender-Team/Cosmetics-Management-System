using System.Buffers.Binary;
using System.Text;
using Whisper.net;

namespace TenderSystem.Services.Voice;

/// <summary>Transcribes short, validated 16 kHz mono 16-bit PCM WAV audio using a local multilingual model.</summary>
public sealed class VoiceTranscriptionService : IDisposable
{
    private const int MaxAudioBytes = 30 * 16000 * 2; // 30 seconds
    private const int MaxFileBytes = MaxAudioBytes + 16384; // Allow small WAV metadata chunks.
    private readonly string? _modelPath;
    private readonly Lazy<WhisperFactory> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public VoiceTranscriptionService(IConfiguration configuration)
    {
        _modelPath = configuration["VOICE_MODEL_PATH"];
        _factory = new Lazy<WhisperFactory>(() =>
        {
            if (string.IsNullOrWhiteSpace(_modelPath) || !Path.IsPathFullyQualified(_modelPath))
                throw new InvalidOperationException("Set VOICE_MODEL_PATH to the full path of a local multilingual Whisper model.");
            if (!File.Exists(_modelPath))
                throw new FileNotFoundException("The configured Whisper model was not found.", _modelPath);
            return WhisperFactory.FromPath(_modelPath);
        });
    }

    /// <summary>Returns recognized text. The caller must authorize and limit uploads before calling this method.</summary>
    public async Task<string> TranscribeAsync(Stream audio, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        using var wav = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await audio.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (wav.Length + read > MaxFileBytes)
                throw new ArgumentException("Audio must be a WAV file of no more than 30 seconds.", nameof(audio));
            await wav.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        ValidateWave(wav);
        wav.Position = 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var processor = _factory.Value.CreateBuilder().WithLanguage("auto").Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(wav, cancellationToken))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateWave(MemoryStream wav)
    {
        var bytes = wav.ToArray();
        if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" ||
            Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE" ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != bytes.Length - 8)
            throw new ArgumentException("Audio must be a valid PCM WAV file.");

        var hasFormat = false;
        var hasData = false;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var start = offset + 8;
            if (size > bytes.Length - start)
                throw new ArgumentException("Audio contains an invalid WAV chunk.");
            var chunk = bytes.AsSpan(start, (int)size);
            var tag = Encoding.ASCII.GetString(bytes, offset, 4);
            if (tag == "fmt ")
            {
                if (hasFormat || size < 16 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk[..2]) != 1 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2, 2)) != 1 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(4, 4)) != 16000 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(8, 4)) != 32000 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(12, 2)) != 2 ||
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14, 2)) != 16)
                    throw new ArgumentException("Audio must be 16 kHz mono 16-bit PCM WAV.");
                hasFormat = true;
            }
            else if (tag == "data")
            {
                if (hasData || size == 0 || size > MaxAudioBytes || size % 2 != 0)
                    throw new ArgumentException("Audio must contain no more than 30 seconds of PCM samples.");
                hasData = true;
            }
            offset = start + (int)size + ((int)size & 1);
            if (offset > bytes.Length)
                throw new ArgumentException("Audio contains an invalid WAV chunk.");
        }
        if (!hasFormat || !hasData)
            throw new ArgumentException("Audio must contain PCM format and sample data.");
    }

    public void Dispose()
    {
        if (_factory.IsValueCreated) _factory.Value.Dispose();
        _gate.Dispose();
    }
}
