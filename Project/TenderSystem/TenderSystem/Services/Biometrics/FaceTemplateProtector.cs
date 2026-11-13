using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Biometrics;

/// <summary>
/// Encrypts the face template (the list of numbers) before it goes into the database, and decrypts it for matching.
/// AES-256-GCM: even someone who copies the database cannot read the templates without the key.
///   Key = FACE_TEMPLATE_ENCRYPTION_KEY in .env (32 random bytes, base64).
///   In Development, if the key is missing or still "CHANGE_ME", a key file is created once in App_Data/face-dev.key
///   (git-ignored) so the feature works out of the box. In any other environment a missing key is an error.
/// Stored bytes: 12 bytes nonce + 16 bytes tag + the encrypted numbers.
/// </summary>
public class FaceTemplateProtector
{
    private readonly Lazy<byte[]> _key;

    public FaceTemplateProtector(IOptions<FaceOptions> options, IHostEnvironment env)
    {
        _key = new Lazy<byte[]>(() => LoadKey(options.Value.TemplateEncryptionKey, env));
    }

    private static byte[] LoadKey(string configured, IHostEnvironment env)
    {
        if (TryDecode(configured, out var key)) return key;

        if (!env.IsDevelopment())
            throw new InvalidOperationException("FACE_TEMPLATE_ENCRYPTION_KEY must be 32 random bytes in base64 (see .env.example).");

        var folder = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "face-dev.key");
        if (File.Exists(file) && TryDecode(File.ReadAllText(file).Trim(), out var existing)) return existing;

        var created = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(file, Convert.ToBase64String(created));
        return created;
    }

    private static bool TryDecode(string? text, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(text) || text == "CHANGE_ME") return false;
        try { key = Convert.FromBase64String(text.Trim()); return key.Length == 32; }
        catch (FormatException) { return false; }
    }

    public byte[] Protect(float[] embedding)
    {
        var plain = MemoryMarshal.AsBytes(embedding.AsSpan()).ToArray();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];

        using var aes = new AesGcm(_key.Value, 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        return [.. nonce, .. tag, .. cipher];
    }

    /// <exception cref="CryptographicException">wrong key or damaged data</exception>
    public float[] Unprotect(byte[] blob)
    {
        if (blob.Length <= 28) throw new CryptographicException("Template too short.");
        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var cipher = blob.AsSpan(28);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key.Value, 16);
        aes.Decrypt(nonce, cipher, tag, plain);

        var result = new float[plain.Length / 4];
        Buffer.BlockCopy(plain, 0, result, 0, plain.Length);
        return result;
    }
}
