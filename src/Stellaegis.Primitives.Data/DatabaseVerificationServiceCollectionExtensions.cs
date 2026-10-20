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
    /// data source the adapter registers, for instance. An adapter calls it from its own registration, beside its
    /// capabilities:
    /// <code>
    /// services.AddStellaegisModuleDatabase(DocumentsDatabase.Value,
    ///     (provider, ct) => provider.GetRequiredService&lt;NpgsqlDataSource&gt;().OpenConnectionAsync(ct).AsTask());
    /// </code>
    /// </summary>
    public static IServiceCollection AddStellaegisModuleDatabase(
        this IServiceCollection services, ModuleDatabase database, Func<IServiceProvider, CancellationToken, Task<DbConnection>> openAsApplication)
    {
        Guard.NotNull(services);
        services.AddSingleton(new ModuleDatabaseRegistration(Guard.NotNull(database), Guard.NotNull(openAsApplication)));
        return services;
    }

    /// <summary>
    /// Registers the verification of every module database at startup, under the application's role: the host
    /// fails to start when a database does not hold with its declaration and the frame
    /// (<see cref="DatabaseLifecycle.VerifyAsync"/>). Registering it twice registers it once.
    /// </summary>
    /// <remarks>
    /// A generic host runs its hosted services before anything else. An ASP.NET Core host starts its server
    /// first: to verify before serving a request, call <see cref="VerifyStellaegisDatabasesAsync"/> on the built
    /// application instead, before running it.
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
        var openers = registrations.ToDictionary(registration => registration.Database, registration => registration.OpenAsApplication);
        await DatabaseLifecycle.VerifyAsync(
            registrations.Select(registration => registration.Database),
            services.GetStellaegisFrame(),
            (database, ct) => openers[database](services, ct),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Fails the host at startup when a module database does not hold with its declaration and the frame.</summary>
internal sealed class DatabaseVerificationHostedService(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => services.VerifyStellaegisDatabasesAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
