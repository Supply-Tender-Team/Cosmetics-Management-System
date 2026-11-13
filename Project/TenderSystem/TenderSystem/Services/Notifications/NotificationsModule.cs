using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Notifications;

public class NotificationsModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<NotificationService>();
    }
}