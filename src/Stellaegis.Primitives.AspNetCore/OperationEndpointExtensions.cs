using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>Reads the <see cref="Operation"/> of a route, and attaches the consumer's security to it.</summary>
public static class OperationEndpointExtensions
{
    /// <summary>
    /// The operation the route is labelled with — directly, or by an <see cref="OperationAttribute"/> on its action or
    /// controller, the closest label counting —, or <see langword="null"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">An <see cref="OperationAttribute"/> names no operation.</exception>
    public static Operation? GetOperation(this Endpoint endpoint)
        => OperationOf(Guard.NotNull(endpoint).Metadata);

    /// <summary>The operation of the route the request reached, or <see langword="null"/>.</summary>
    public static Operation? GetOperation(this HttpContext httpContext)
        => Guard.NotNull(httpContext).GetEndpoint()?.GetOperation();

    /// <summary>
    /// Requires, on each route labelled with an <see cref="Operation"/>, the policy that
    /// <paramref name="policyFor"/> names for it — usually the operation's identity, one policy per operation:
    /// <code>
    /// app.MapGroup("/api").MapDocumentsEndpoints()
    ///    .RequireAuthorizationPerOperation(op => op.ToString());
    /// </code>
    /// An application without rights of its own may choose by kind instead, a coarse fallback:
    /// <c>op => op.Kind == OperationKind.Read ? "reader" : "writer"</c>. A route without an operation is left to the
    /// host's fallback policy, if it sets one.
    /// </summary>
    /// <remarks>
    /// A final convention: a group convention runs before a route has its own metadata, so only a final one
    /// sees the operation. It runs when the endpoints are built; the startup check
    /// (<c>AddStellaegisStartupCheck</c>) verifies that every policy it names exists.
    /// </remarks>
    public static TBuilder RequireAuthorizationPerOperation<TBuilder>(this TBuilder builder, Func<Operation, string> policyFor)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        Guard.NotNull(policyFor);

        builder.Finally(endpoint =>
        {
            if (OperationOf(endpoint.Metadata) is { } operation)
                endpoint.Metadata.Add(new AuthorizeAttribute(policyFor(operation)));
        });
        return builder;
    }

    private static Operation? OperationOf(IEnumerable<object> metadata)
        => metadata.LastOrDefault(item => item is Operation or OperationAttribute) switch
        {
            Operation operation => operation,
            OperationAttribute attribute => attribute.Operation,
            _ => null,
        };

    /// <summary>
    /// Declares that any user reaching these routes may access any resource they load: the module does not ask
    /// the authorization on each resource. The equivalent, for data, of <c>[AllowAnonymous]</c> — or the
    /// declaration of an application whose database already filters what a route loads, by row-level security.
    /// </summary>
    /// <remarks>
    /// Without it, a module asks the authorization on each resource it loads, and refuses unless a handler of
    /// the consumer succeeds. A module mapped with neither this declaration nor a handler fails the startup.
    /// </remarks>
    public static TBuilder AllowAnyResource<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(AllowAnyResourceMetadata.Instance);
    }

    /// <summary>
    /// Declares that these routes act <b>on</b> spaces rather than in one — purging a compartment, reading a secret of
    /// any compartment by its path: the space is the object of the route, read from its path or its body, never
    /// its context. A module declares it on its administration routes:
    /// <code>
    /// var admin = routes.MapGroup("/documents").BelongsTo&lt;DocumentsModule&gt;().RequireRequestContext().ActsOnSpaces();
    /// admin.MapDelete("/compartments/{compartment}", ...).WithMetadata(Purge).LoadsNoResource();
    /// </code>
    /// </summary>
    /// <remarks>
    /// On such a route, <see cref="IRequestContext.Space"/> fails — the route reads its object itself — and
    /// <see cref="IRequestContext.Actor"/> stays. The startup check refuses it in the shared space, which is a place
    /// and not an object, and requires of it a module and an operation, as of any labelled route. Who may call it
    /// stays the application's decision, as everywhere.
    /// </remarks>
    public static TBuilder ActsOnSpaces<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(ActsOnSpacesMetadata.Instance);
    }

    /// <summary>
    /// Declares that these routes load no resource — a purge acting on a compartment, a rendering of the request's
    /// own content: there is nothing to ask the authorization on. A module declares it on such a route, and the
    /// startup check requires of it neither a handler for the resources nor <c>AllowAnyResource</c>.
    /// <code>
    /// admin.MapDelete("/compartments/{compartment}", ...).WithMetadata(Purge).LoadsNoResource();
    /// </code>
    /// </summary>
    /// <remarks>
    /// The route itself stays closed until the application declares its authorization: this concerns the data, not
    /// who may call. <c>AuthorizeResourceAsync</c> fails on such a route, which declared it would not ask.
    /// </remarks>
    public static TBuilder LoadsNoResource<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(LoadsNoResourceMetadata.Instance);
    }
}

/// <summary>The metadata <see cref="OperationEndpointExtensions.ActsOnSpaces{TBuilder}"/> places on a route.</summary>
internal sealed class ActsOnSpacesMetadata
{
    public static readonly ActsOnSpacesMetadata Instance = new();

    private ActsOnSpacesMetadata()
    {
    }
}

/// <summary>The metadata <see cref="OperationEndpointExtensions.LoadsNoResource{TBuilder}"/> places on a route.</summary>
internal sealed class LoadsNoResourceMetadata
{
    public static readonly LoadsNoResourceMetadata Instance = new();

    private LoadsNoResourceMetadata()
    {
    }
}

/// <summary>The metadata <see cref="OperationEndpointExtensions.AllowAnyResource{TBuilder}"/> places on a route.</summary>
internal sealed class AllowAnyResourceMetadata
{
    public static readonly AllowAnyResourceMetadata Instance = new();

    private AllowAnyResourceMetadata()
    {
    }
}
