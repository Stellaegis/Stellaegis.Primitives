using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>Reads the <see cref="Operation"/> of a route, and attaches the consumer's security to it.</summary>
public static class OperationEndpointExtensions
{
    /// <summary>The operation the route is labelled with, or <see langword="null"/>.</summary>
    public static Operation? GetOperation(this Endpoint endpoint)
        => Guard.NotNull(endpoint).Metadata.GetMetadata<Operation>();

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
            if (endpoint.Metadata.OfType<Operation>().LastOrDefault() is { } operation)
                endpoint.Metadata.Add(new AuthorizeAttribute(policyFor(operation)));
        });
        return builder;
    }

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
}

/// <summary>The metadata <see cref="OperationEndpointExtensions.AllowAnyResource{TBuilder}"/> places on a route.</summary>
internal sealed class AllowAnyResourceMetadata
{
    public static readonly AllowAnyResourceMetadata Instance = new();

    private AllowAnyResourceMetadata()
    {
    }
}
