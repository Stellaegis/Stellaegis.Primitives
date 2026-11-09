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
    /// does when it starts — before any hosted service starts, whatever the order of their registrations: a host
    /// whose declarations do not hold fails to start, naming what to change. An
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

/// <summary>
/// Fails the host at startup when the frame and the modules do not hold together: while the host is starting, before
/// any hosted service starts — a purge registered first would otherwise run under an assembly that does not hold.
/// </summary>
internal sealed class ValidationHostedService(IServiceProvider services) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        services.ValidateStellaegisModules();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
