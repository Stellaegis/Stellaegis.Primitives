using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Whether a group of routes, or a controller, accepts to be mapped in the shared space — what a route the
/// application marks with <see cref="SharedSpaceEndpointExtensions.InSharedSpace{TBuilder}"/> is checked against.
/// </summary>
public enum SharedSpaceAcceptance
{
    /// <summary>As the module declares, <see cref="ModuleDeclaration.AcceptsSharedSpace"/>: the usual case.</summary>
    OfModule,

    /// <summary>Accepted, whatever the module declares — routes of the application's own in the shared space, for instance.</summary>
    Accepted,

    /// <summary>Refused, whatever the module declares — the administration routes of a module that accepts it, for instance.</summary>
    Refused,
}

/// <summary>What a module declares about the request context when it maps its endpoints.</summary>
public static class RequestContextEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Declares that the endpoints of <paramref name="group"/> read the request context. Fails the startup when no
    /// <see cref="IRequestContext"/> is registered, naming the line to add. A module with compartments calls it on
    /// the group it maps onto, after declaring the module:
    /// <code>
    /// public static RouteGroupBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder routes)
    /// {
    ///     var group = routes.MapGroup("/documents")
    ///         .BelongsTo&lt;DocumentsModule&gt;()
    ///         .RequireRequestContext();
    ///     // ...
    /// }
    /// </code>
    /// </summary>
    /// <param name="group">The group holding the module's endpoints.</param>
    /// <param name="sharedSpace">
    /// Whether the endpoints may be mapped in the shared space. By default, as the module declares with
    /// <see cref="ModuleDeclaration.AcceptingSharedSpace"/>; a sub-group declares again to differ — the
    /// administration routes of a module that accepts it refuse it —, and routes of the application's own accept it
    /// themselves. The declaration closest to a route counts.
    /// </param>
    /// <returns><paramref name="group"/>.</returns>
    /// <remarks>
    /// The registration check relies on <see cref="IServiceProviderIsService"/>, which the default container
    /// implements. With a container that does not, it is skipped, and a missing registration surfaces at the
    /// first request instead.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No request context is registered.</exception>
    /// <seealso cref="RequiresRequestContextAttribute"/>
    public static RouteGroupBuilder RequireRequestContext(this RouteGroupBuilder group, SharedSpaceAcceptance sharedSpace = SharedSpaceAcceptance.OfModule)
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
        return group.WithMetadata(new RequiresRequestContextAttribute { SharedSpace = sharedSpace });
    }
}

/// <summary>
/// Declares that the actions of a controller, or one action, read the request context, and whether they accept
/// the shared space — what <see cref="RequestContextEndpointRouteBuilderExtensions.RequireRequestContext"/>
/// declares on a group of routes:
/// <code>
/// [ApiController, Route("notes"), BelongsTo&lt;NotesModule&gt;, RequiresRequestContext]
/// public sealed class NotesController : ControllerBase { /* ... */ }
/// </code>
/// The last declaration on a route — the closest to it, an action's over its controller's — is the one that counts.
/// </summary>
/// <remarks>
/// Unlike the group method, the attribute cannot check at mapping time that a request context is registered: the
/// startup check refuses the route instead, naming it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequiresRequestContextAttribute : Attribute
{
    /// <summary>
    /// Whether the routes may be mapped in the shared space: as the module declares by default, accepted or refused
    /// whatever it declares. A route the application marks with
    /// <see cref="SharedSpaceEndpointExtensions.InSharedSpace{TBuilder}"/> fails the startup unless the declaration
    /// closest to it, or its module, accepts the shared space.
    /// </summary>
    public SharedSpaceAcceptance SharedSpace { get; init; }
}
