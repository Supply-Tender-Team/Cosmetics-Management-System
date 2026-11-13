using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Email;

/// <summary>Registers the email service. Found automatically by Program.cs (IFeatureModule).</summary>
public class EmailModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EmailService>();
    }
}
