using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>What a module declares about the request context when it maps its endpoints.</summary>
public static class RequestContextEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Declares that the endpoints of <paramref name="group"/> read the request context, and whether they accept
    /// the shared space. Fails the startup when no <see cref="IRequestContext"/> is registered, naming the line to
    /// add. A module with compartments calls it on the group it maps onto, after declaring the module:
    /// <code>
    /// public static RouteGroupBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
    /// {
    ///     var group = routes.MapGroup("/documents")
    ///         .BelongsTo&lt;DocumentsModule&gt;()
    ///         .RequireRequestContext(acceptsSharedSpace: true);
    ///     // ...
    /// }
    /// </code>
    /// </summary>
    /// <param name="group">The group holding the module's endpoints.</param>
    /// <param name="acceptsSharedSpace">
    /// Whether the endpoints may be mapped in the shared space. Refused by default: a route the application
    /// marks with <see cref="SharedSpaceEndpointExtensions.InSharedSpace{TBuilder}"/> fails the startup unless the
    /// declaration closest to it accepts the shared space. A sub-group declares again to differ from its parent.
    /// </param>
    /// <returns><paramref name="group"/>.</returns>
    /// <remarks>
    /// The registration check relies on <see cref="IServiceProviderIsService"/>, which the default container
    /// implements. With a container that does not, it is skipped, and a missing registration surfaces at the
    /// first request instead.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No request context is registered.</exception>
    public static RouteGroupBuilder RequireRequestContext(this RouteGroupBuilder group, bool acceptsSharedSpace = false)
    {
        Guard.NotNull(group);

        IEndpointRouteBuilder routes = group;
        if (routes.ServiceProvider.GetService<IServiceProviderIsService>() is { } services
            && !services.IsService(typeof(IRequestContext)))
        {
            throw new InvalidOperationException(
                "These endpoints need a request context, giving the compartment and the actor of each request. "
                + "Register one before mapping them, with the application's frame: "
                + "builder.Services.AddStellaegisFrame(ApplicationFrame.SingleCompartment(\"acme\")).AddStellaegisRequestContext(), "
                + "or a compartment source when the frame has many — AddStellaegisRequestContext(c => c.CompartmentFromRoute(\"org\")) — "
                + "or AddStellaegisRequestContext<TSource>() with a source of its own.");
        }
        return group.WithMetadata(new RequestContextDeclaration(acceptsSharedSpace));
    }
}

/// <summary>
/// The metadata <see cref="RequestContextEndpointRouteBuilderExtensions.RequireRequestContext"/> places on a route.
/// The last one on a route — the declaration closest to it — is the one that counts.
/// </summary>
internal sealed record RequestContextDeclaration(bool AcceptsSharedSpace);
