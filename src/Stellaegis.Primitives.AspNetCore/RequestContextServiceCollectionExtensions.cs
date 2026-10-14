using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>Registers the <see cref="IRequestContext"/> adapter of an application, scoped to the request.</summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers the configurable request context, reading the compartment and the actor from the sources
    /// declared by <paramref name="configure"/>:
    /// <code>
    /// builder.Services.AddStellaegisRequestContext(c => c.SingleCompartment("main"));
    /// </code>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No compartment source, or more than one, is declared; or a request context is already registered.
    /// </exception>
    public static IServiceCollection AddStellaegisRequestContext(
        this IServiceCollection services,
        Action<RequestContextBuilder> configure)
    {
        Guard.NotNull(services);
        Guard.NotNull(configure);
        EnsureNotRegistered(services);

        var builder = new RequestContextBuilder();
        configure(builder);
        var sources = builder.Build();

        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext>(provider =>
            new ConfiguredRequestContext(provider.GetRequiredService<IHttpContextAccessor>(), sources));
        return services;
    }

    /// <summary>
    /// Registers an adapter of the application's own — one that reads its tenant resolution, for instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">A request context is already registered.</exception>
    public static IServiceCollection AddStellaegisRequestContext<TAdapter>(this IServiceCollection services)
        where TAdapter : class, IRequestContext
    {
        Guard.NotNull(services);
        EnsureNotRegistered(services);

        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, TAdapter>();
        return services;
    }

    private static void EnsureNotRegistered(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IRequestContext)))
            throw new InvalidOperationException(
                "A request context is already registered: an application declares a single source of compartment and actor.");
    }
}
