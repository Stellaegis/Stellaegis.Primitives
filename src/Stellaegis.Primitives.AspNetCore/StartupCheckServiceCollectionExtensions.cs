using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>Registers the startup check of the routes.</summary>
public static class StartupCheckServiceCollectionExtensions
{
    /// <summary>
    /// Registers the startup check of the routes, which fails the application before it serves anything when a
    /// route's declarations do not hold: two routes share a name; a route in the shared space is not declared to
    /// accept it; a route labelled with an <see cref="Operation"/> belongs to no module, declares no
    /// authorization, has neither an authorization handler for its resources nor allows any resource, or
    /// requires a policy that does not exist.
    /// </summary>
    /// <remarks>
    /// <c>AddStellaegisRequestContext</c> registers it too. A module without compartments calls this one from its
    /// own registration, so that an application using only such modules is checked all the same. Registering it
    /// twice is harmless.
    /// </remarks>
    public static IServiceCollection AddStellaegisStartupCheck(this IServiceCollection services)
    {
        Guard.NotNull(services);
        services.TryAddSingleton<EndpointStartupCheck>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IStartupFilter, EndpointStartupCheck>());
        return services;
    }
}
