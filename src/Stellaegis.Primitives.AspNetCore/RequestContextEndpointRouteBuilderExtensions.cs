using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>Startup checks a module runs when it maps its endpoints.</summary>
public static class RequestContextEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Fails the startup when no <see cref="IRequestContext"/> is registered, naming the line to add. A module
    /// calls it first in its mapping method:
    /// <code>
    /// public static RouteGroupBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
    /// {
    ///     routes.RequireRequestContext("Documents");
    ///     // ...
    /// }
    /// </code>
    /// </summary>
    /// <param name="routes">The routes the module maps onto.</param>
    /// <param name="module">The module name, as the error message shows it.</param>
    /// <returns><paramref name="routes"/>, unchanged.</returns>
    /// <remarks>
    /// The check relies on <see cref="IServiceProviderIsService"/>, which the default container implements.
    /// With a container that does not, it is skipped, and a missing registration surfaces at the first
    /// request instead.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No request context is registered.</exception>
    public static IEndpointRouteBuilder RequireRequestContext(this IEndpointRouteBuilder routes, string module)
    {
        Guard.NotNull(routes);
        Guard.NotWhiteSpace(module);

        if (routes.ServiceProvider.GetService<IServiceProviderIsService>() is { } services
            && !services.IsService(typeof(IRequestContext)))
        {
            throw new InvalidOperationException(
                $"{module} endpoints need a request context, giving the compartment and the actor of each request. "
                + "Register one before mapping them: builder.Services.AddStellaegisRequestContext(c => c.SingleCompartment(\"main\")), "
                + "with the compartment source that fits the application, or AddStellaegisRequestContext<TAdapter>().");
        }
        return routes;
    }
}
