namespace TenderSystem.Infrastructure;

/// <summary>
/// Each feature owner registers his own services in a class that implements this interface.
/// Program.cs discovers every implementation automatically, so nobody edits Program.cs.
///
/// Example (create it inside YOUR feature folder, e.g. Services/Requests/RequestsModule.cs):
///
///     public class RequestsModule : IFeatureModule
///     {
///         public void Register(IServiceCollection services, IConfiguration config)
///         {
///             services.AddScoped&lt;IRequestService, RequestService&gt;();
///         }
///     }
/// </summary>
public interface IFeatureModule
{
    void Register(IServiceCollection services, IConfiguration configuration);
}

public static class FeatureModuleExtensions
{
    public static IServiceCollection AddFeatureModules(this IServiceCollection services, IConfiguration configuration)
    {
        var modules = typeof(IFeatureModule).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFeatureModule).IsAssignableFrom(t))
            .Select(t => (IFeatureModule)Activator.CreateInstance(t)!);

        foreach (var module in modules)
            module.Register(services, configuration);

        return services;
    }
}
