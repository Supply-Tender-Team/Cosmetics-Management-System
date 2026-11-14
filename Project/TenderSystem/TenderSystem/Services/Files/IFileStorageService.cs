namespace TenderSystem.Services.Files;

public sealed record StoredFile(string OriginalName, string StoredName, string RelativePath, string ContentType, long SizeBytes);

public interface IFileStorageService
{
    Task<StoredFile> SaveAsync(Stream content, string originalName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
