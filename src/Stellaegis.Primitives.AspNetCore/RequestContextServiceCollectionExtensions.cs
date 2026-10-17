using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Registers the <see cref="IRequestContextSource"/> of an application and the <see cref="IRequestContext"/> read
/// from it, both scoped to the request, along with the startup check of the routes
/// (<see cref="StartupCheckServiceCollectionExtensions.AddStellaegisStartupCheck"/>).
/// </summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers the configurable request context source, reading the compartment and the actor from the
    /// sources declared by <paramref name="configure"/>:
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
        var settings = builder.Build();

        AddCommon(services);
        services.AddScoped<IRequestContextSource>(provider =>
            new ConfiguredRequestContextSource(provider.GetRequiredService<IHttpContextAccessor>(), settings));
        return services;
    }

    /// <summary>
    /// Registers a source of the application's own — one that reads its tenant resolution, for instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">A request context is already registered.</exception>
    public static IServiceCollection AddStellaegisRequestContext<TSource>(this IServiceCollection services)
        where TSource : class, IRequestContextSource
    {
        Guard.NotNull(services);
        EnsureNotRegistered(services);

        AddCommon(services);
        services.AddScoped<IRequestContextSource, TSource>();
        return services;
    }

    // An application with a request context is checked at startup without registering the check itself.
    private static void AddCommon(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, RequestContext>();
        services.AddStellaegisStartupCheck();
    }

    private static void EnsureNotRegistered(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IRequestContext)
                || descriptor.ServiceType == typeof(IRequestContextSource)))
        {
            throw new InvalidOperationException(
                "A request context is already registered: an application declares a single source of compartment and actor.");
        }
    }
}
