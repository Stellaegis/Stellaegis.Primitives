using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Stellaegis.Primitives.Hosting;

/// <summary>Validates the assembly when a host without ASP.NET Core starts — a worker, a scheduled task.</summary>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the validation of the frame against the modules
    /// (<see cref="StellaegisServiceProviderExtensions.ValidateStellaegisModules"/>) as the first thing the host
    /// does when it starts: a host whose declarations do not hold fails to start, naming what to change. An
    /// ASP.NET Core host validates by itself, with the startup check of its routes; a generic host calls this
    /// once. Registering it twice registers it once.
    /// <code>
    /// builder.Services.AddStellaegisFrame(AppFrame.Value).AddStellaegisValidation();
    /// </code>
    /// </summary>
    public static IServiceCollection AddStellaegisValidation(this IServiceCollection services)
    {
        Guard.NotNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ValidationHostedService>());
        return services;
    }
}

/// <summary>Fails the host at startup when the frame and the modules do not hold together.</summary>
internal sealed class ValidationHostedService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        services.ValidateStellaegisModules();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
