using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Auth;

/// <summary>Registers the password service. Found automatically by Program.cs (IFeatureModule).</summary>
public class AuthModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PasswordService>();
    }
}
