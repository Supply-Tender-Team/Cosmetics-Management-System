using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Email;

/// <summary>Registers the email outbox (EML-01). Found automatically by Program.cs (IFeatureModule).</summary>
public class EmailOutboxModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOutboxOptions>(o =>
        {
            o.Enabled = Env.GetBool("EMAIL_OUTBOX_ENABLED", o.Enabled);
            o.IntervalSeconds = Env.GetInt("EMAIL_OUTBOX_INTERVAL_SECONDS", o.IntervalSeconds);
            o.MaxAttempts = Env.GetInt("EMAIL_OUTBOX_MAX_ATTEMPTS", o.MaxAttempts);
            o.Batch = Env.GetInt("EMAIL_OUTBOX_BATCH", o.Batch);
            o.LockMinutes = Env.GetInt("EMAIL_OUTBOX_LOCK_MINUTES", o.LockMinutes);
        });
        services.AddScoped<EmailOutboxService>();
        services.AddScoped<EmailOutboxProcessor>();
        services.AddHostedService<EmailOutboxWorker>();
    }
}
