using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Stellaegis.Primitives.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Registers the <see cref="IRequestContextSource"/> of an application and the <see cref="IRequestContext"/> read
/// from it and from the application frame, both scoped to the request, along with the startup check of the routes
/// (<see cref="StartupCheckServiceCollectionExtensions.AddStellaegisStartupCheck"/>).
/// </summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers the configurable request context source. The frame says how many compartments there are; this
    /// says where a request reads its own, when there are many, and where it reads its actor:
    /// <code>
    /// builder.Services.AddStellaegisFrame(ApplicationFrame.SingleCompartment("acme"));
    /// builder.Services.AddStellaegisRequestContext();
    ///
    /// builder.Services.AddStellaegisFrame(ApplicationFrame.ManyCompartments());
    /// builder.Services.AddStellaegisRequestContext(c => c.CompartmentFromRoute("org"));
    /// </code>
    /// </summary>
    /// <remarks>
    /// The declaration is read against the frame at startup, which fails when they disagree: a compartment source in
    /// an application with a single compartment, none in an application with many, or a request context in an
    /// application without compartments.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// More than one compartment source is declared, or a request context is already registered.
    /// </exception>
    public static IServiceCollection AddStellaegisRequestContext(
        this IServiceCollection services,
        Action<RequestContextBuilder>? configure = null)
    {
        Guard.NotNull(services);
        EnsureNotRegistered(services);

        var builder = new RequestContextBuilder();
        configure?.Invoke(builder);
        var settings = builder.Build();

        AddCommon(services);
        services.AddSingleton(settings);
        services.AddScoped<IRequestContextSource>(provider => new ConfiguredRequestContextSource(
            provider.GetRequiredService<IHttpContextAccessor>(), settings, provider.GetStellaegisFrame().AnonymousActor));
        return services;
    }

    /// <summary>
    /// Registers a source of the application's own — one that reads its tenant resolution, for instance. In an
    /// application with a single compartment, the frame gives the compartment, and the source only the actor.
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
        services.AddScoped<IRequestContext>(provider => new RequestContext(
            provider.GetRequiredService<IHttpContextAccessor>(),
            provider.GetRequiredService<IRequestContextSource>(),
            provider.GetStellaegisFrame()));
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
