using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Biometrics;

/// <summary>Registers the face services. Found automatically by Program.cs (IFeatureModule).</summary>
public class BiometricsModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<FaceEngine>();            // loads the AI models once
        services.AddSingleton<FaceTemplateProtector>(); // AES key handling
        services.AddScoped<FaceService>();
    }
}
