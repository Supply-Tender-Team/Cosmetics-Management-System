using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Files;

public sealed class FilesModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient<IFileStorageService, FileStorageService>();
}
