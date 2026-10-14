using Microsoft.AspNetCore.Builder;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>Marks the routes that address the shared space.</summary>
public static class SharedSpaceEndpointExtensions
{
    /// <summary>
    /// Declares that these routes address the shared space: on them, <see cref="IRequestContext.Space"/> is
    /// <see cref="Space.Shared"/>, whatever the <see cref="IRequestContextSource"/> would resolve.
    /// <code>
    /// app.MapGroup("/api/orgs/{org}").MapDocumentsEndpoints();
    /// app.MapGroup("/api/shared").MapDocumentsEndpoints().InSharedSpace();
    /// </code>
    /// </summary>
    /// <remarks>
    /// The mark is the only way into the shared space. A group mapped without it reads the compartment of the
    /// source, and answers 404 where there is none.
    /// </remarks>
    public static TBuilder InSharedSpace<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(SharedSpaceMetadata.Instance);
    }
}

/// <summary>The metadata <see cref="SharedSpaceEndpointExtensions.InSharedSpace{TBuilder}"/> places on a route.</summary>
internal sealed class SharedSpaceMetadata
{
    public static readonly SharedSpaceMetadata Instance = new();

    private SharedSpaceMetadata()
    {
    }
}
