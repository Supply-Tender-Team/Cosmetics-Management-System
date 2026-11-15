using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Voice;

public class VoiceModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<VoiceTranscriptionService>();
    }
}
