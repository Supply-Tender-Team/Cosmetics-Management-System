using DotNetEnv;
using TenderSystem.Infrastructure;

namespace TenderSystem.Services.Ai;

/// <summary>
/// Settings of the assistant's command center (AI-04). Own class and own module, so nobody has to edit shared files.
///   AI_ACTIONS=false              OFF by default. true = Admins get a "Command" mode in the chat.
///   AI_ACTION_MAX_RECIPIENTS=100  an action that would reach more people is refused (narrow the audience)
///   AI_ACTION_MAX_PER_HOUR=5      actions one Admin can run per hour, even with a valid fingerprint or face
///   AI_ACTION_TTL_MINUTES=5       how long a proposed action waits for the Admin's approval
///   AI_ACTION_MAX_PROPOSALS_PER_MINUTE=10   how many commands one Admin can type per minute
/// </summary>
public class AiActionOptions
{
    public bool Enabled { get; set; } = false;
    public int MaxRecipients { get; set; } = 100;
    public int MaxPerHour { get; set; } = 5;
    public int TtlMinutes { get; set; } = 5;
    public int MaxProposalsPerMinute { get; set; } = 10;
}

public class AiActionModule : IFeatureModule
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiActionOptions>(o =>
        {
            o.Enabled = Env.GetBool("AI_ACTIONS", o.Enabled);
            o.MaxRecipients = Math.Clamp(Env.GetInt("AI_ACTION_MAX_RECIPIENTS", o.MaxRecipients), 1, 1000);
            o.MaxPerHour = Math.Clamp(Env.GetInt("AI_ACTION_MAX_PER_HOUR", o.MaxPerHour), 1, 100);
            o.TtlMinutes = Math.Clamp(Env.GetInt("AI_ACTION_TTL_MINUTES", o.TtlMinutes), 1, 30);
            o.MaxProposalsPerMinute = Math.Clamp(Env.GetInt("AI_ACTION_MAX_PROPOSALS_PER_MINUTE", o.MaxProposalsPerMinute), 1, 1000);
        });
        services.AddMemoryCache();
        services.AddScoped<AiActionService>();
    }
}
