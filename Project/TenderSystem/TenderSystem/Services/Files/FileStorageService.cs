using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DotNetEnv;
using TenderSystem.Infrastructure.Options;

namespace TenderSystem.Services.Files;

public sealed class FileStorageService : IFileStorageService
{
    private readonly string _root;
    private readonly long _maxBytes;
    private readonly HashSet<string> _allowedExtensions;
    private readonly HttpClient _http;
    private readonly string _bucket;
    private readonly string _host;
    private readonly string _endpoint;
    private readonly string _accessKeyId;
    private readonly string _secretAccessKey;
    private readonly bool _useR2;

    public FileStorageService(IOptions<UploadOptions> options, IWebHostEnvironment environment, HttpClient http)
    {
        _http = http;
        var configuredRoot = options.Value.RootPath;
        _root = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot));
        _maxBytes = Math.Max(1, options.Value.MaxFileSizeMb) * 1024L * 1024L;
        _allowedExtensions = options.Value.AllowedExtensions
            .Select(x => x.StartsWith('.') ? x : $".{x}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var accountId = Env.GetString("R2_ACCOUNT_ID", "");
        _bucket = Env.GetString("R2_BUCKET", "");
        _accessKeyId = Env.GetString("R2_ACCESS_KEY_ID", "");
        _secretAccessKey = Env.GetString("R2_SECRET_ACCESS_KEY", "");
        var configuredValues = new[] { accountId, _bucket, _accessKeyId, _secretAccessKey };
        _useR2 = configuredValues.All(value => !string.IsNullOrWhiteSpace(value));
        if (!_useR2 && configuredValues.Any(value => !string.IsNullOrWhiteSpace(value)))
            throw new InvalidOperationException("R2 settings are incomplete. Set R2_ACCOUNT_ID, R2_BUCKET, R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY.");

        _endpoint = string.IsNullOrWhiteSpace(accountId) ? "" : $"https://{accountId}.r2.cloudflarestorage.com";
        _host = string.IsNullOrWhiteSpace(_endpoint) ? "" : new Uri(_endpoint).Host;
    }

    public async Task<StoredFile> SaveAsync(Stream content, string originalName, string contentType, CancellationToken cancellationToken = default)
    {
        var safeOriginalName = Path.GetFileName(originalName);
        var extension = Path.GetExtension(safeOriginalName);
        if (string.IsNullOrWhiteSpace(extension) || !_allowedExtensions.Contains(extension))
            throw new InvalidOperationException("This file type is not allowed.");

        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        long total = 0;
        await using var bufferStream = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > _maxBytes)
                throw new InvalidOperationException($"The file is larger than {_maxBytes / 1024 / 1024} MB.");
            await bufferStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var bytes = bufferStream.ToArray();
        if (_useR2)
        {
            await UploadToR2Async(storedName, bytes, contentType, cancellationToken);
            return new StoredFile(safeOriginalName, storedName, $"r2:{storedName}", contentType, total);
        }

        Directory.CreateDirectory(_root);
        var fullPath = GetLocalPath(storedName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return new StoredFile(safeOriginalName, storedName, $"local:{storedName}", contentType, total);
    }

    public async Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        if (storagePath.StartsWith("r2:", StringComparison.Ordinal))
        {
            if (!_useR2) throw new InvalidOperationException("This file is stored in R2, but R2 settings are not configured.");
            return await DownloadFromR2Async(storagePath[3..], cancellationToken);
        }

        var localName = storagePath.StartsWith("local:", StringComparison.Ordinal) ? storagePath[6..] : storagePath;
        var fullPath = GetLocalPath(localName);
        return File.Exists(fullPath) ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true) : null;
    }

    private string GetLocalPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The requested file path is invalid.");
        return fullPath;
    }

    private async Task UploadToR2Async(string key, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        using var request = CreateSignedRequest(HttpMethod.Put, key, bytes);
        request.Content = new ByteArrayContent(bytes);
        if (System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            request.Content.Headers.ContentType = mediaType;
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"R2 could not store the file (HTTP {(int)response.StatusCode}). Check the bucket and API token permissions.");
    }

    private async Task<Stream?> DownloadFromR2Async(string key, CancellationToken cancellationToken)
    {
        using var request = CreateSignedRequest(HttpMethod.Get, key, null);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"R2 could not return the file (HTTP {(int)response.StatusCode}). Check the bucket and API token permissions.");

        var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        return copy;
    }

    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string key, byte[]? body)
    {
        var escapedBucket = Uri.EscapeDataString(_bucket);
        var escapedKey = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
        var canonicalUri = $"/{escapedBucket}/{escapedKey}";
        var payloadHash = body is null ? "UNSIGNED-PAYLOAD" : Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = timestamp[..8];
        var canonicalHeaders = $"host:{_host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{timestamp}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method.Method}\n{canonicalUri}\n\n{canonicalHeaders}{signedHeaders}\n{payloadHash}";
        var scope = $"{date}/auto/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{timestamp}\n{scope}\n{HashHex(canonicalRequest)}";
        var signingKey = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + _secretAccessKey), date), "auto"), "s3"), "aws4_request");
        var signature = Convert.ToHexString(Hmac(signingKey, stringToSign)).ToLowerInvariant();

        var request = new HttpRequestMessage(method, $"{_endpoint}{canonicalUri}");
        request.Headers.Host = _host;
        request.Headers.TryAddWithoutValidation("x-amz-date", timestamp);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKeyId}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        return request;
    }

    private static string HashHex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static byte[] Hmac(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }
}
