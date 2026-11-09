using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Primitives.Data;

/// <summary>A module's database as its adapter registers it, with the way the application opens it under its own role.</summary>
public sealed record ModuleDatabaseRegistration(ModuleDatabase Database, Func<IServiceProvider, CancellationToken, Task<DbConnection>> OpenAsApplication);

/// <summary>
/// What an adapter registers about its database, and what the host does with it: the verification at startup,
/// under the application's role.
/// </summary>
public static class DatabaseVerificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database of a module, with the way the application opens it under its own role — from the
    /// data source the adapter registers, for instance —, and its verification at startup
    /// (<see cref="AddStellaegisDatabaseVerification"/>). An adapter calls it from its own registration, beside its
    /// capabilities:
    /// <code>
    /// services.AddStellaegisModuleDatabase(DocumentsDatabase.Value,
    ///     (provider, ct) => provider.GetRequiredService&lt;NpgsqlDataSource&gt;().OpenConnectionAsync(ct).AsTask());
    /// </code>
    /// </summary>
    /// <remarks>
    /// The verification comes with the database, so that no host serves a database it forgot to verify: a rule of
    /// isolation the frame declares and the database lacks would otherwise go unseen. A process that installs the
    /// databases does so before starting a host, or builds none.
    /// </remarks>
    public static IServiceCollection AddStellaegisModuleDatabase(
        this IServiceCollection services, ModuleDatabase database, Func<IServiceProvider, CancellationToken, Task<DbConnection>> openAsApplication)
    {
        Guard.NotNull(services);
        services.AddSingleton(new ModuleDatabaseRegistration(Guard.NotNull(database), Guard.NotNull(openAsApplication)));
        return services.AddStellaegisDatabaseVerification();
    }

    /// <summary>
    /// Registers the verification of every module database at startup, under the application's role: the host
    /// fails to start when a database does not hold with its declaration and the frame
    /// (<see cref="DatabaseLifecycle.VerifyAsync"/>). <see cref="AddStellaegisModuleDatabase"/> registers it; registering
    /// it again registers it once.
    /// </summary>
    /// <remarks>
    /// The verification runs while the host is starting, before any hosted service starts — the server of an ASP.NET
    /// Core host included —, so that nothing is served nor run on a database that does not hold.
    /// </remarks>
    public static IServiceCollection AddStellaegisDatabaseVerification(this IServiceCollection services)
    {
        Guard.NotNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseVerificationHostedService>());
        return services;
    }

    /// <summary>The databases the modules registered.</summary>
    public static IReadOnlyList<ModuleDatabase> GetStellaegisModuleDatabases(this IServiceProvider services)
        => Guard.NotNull(services).GetServices<ModuleDatabaseRegistration>().Select(registration => registration.Database).ToList();

    /// <summary>
    /// Verifies every module database under the application's role, against the registered frame, and fails
    /// naming every module and what is missing. An ASP.NET Core host calls it before running:
    /// <code>
    /// var app = builder.Build();
    /// await app.Services.VerifyStellaegisDatabasesAsync();
    /// app.Run();
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">A database does not hold with its declaration and the frame.</exception>
    public static async Task VerifyStellaegisDatabasesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(services);
        var registrations = services.GetServices<ModuleDatabaseRegistration>().ToList();
        if (registrations.Count == 0)
            return;
        var openers = new Dictionary<ModuleDatabase, Func<IServiceProvider, CancellationToken, Task<DbConnection>>>();
        foreach (var registration in registrations)
            openers.TryAdd(registration.Database, registration.OpenAsApplication);   // twice, DatabaseLifecycle says so
        await DatabaseLifecycle.VerifyAsync(
            registrations.Select(registration => registration.Database),
            services.GetStellaegisFrame(),
            (database, ct) => openers[database](services, ct),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Fails the host at startup when a module database does not hold with its declaration and the frame: while the host
/// is starting, before any hosted service starts — the server of an ASP.NET Core host included.
/// </summary>
internal sealed class DatabaseVerificationHostedService(IServiceProvider services) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => services.VerifyStellaegisDatabasesAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
