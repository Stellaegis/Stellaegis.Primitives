namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Where an application reads the compartment and the actor of a request — its tenant resolution, for instance.
/// The application implements it and registers it with <c>AddStellaegisRequestContext&lt;TSource&gt;()</c>
/// (<see cref="RequestContextServiceCollectionExtensions"/>); modules never read it, they read
/// <see cref="IRequestContext"/>.
/// </summary>
/// <remarks>
/// A source knows nothing of the shared space: only a route the application marks with
/// <see cref="SharedSpaceEndpointExtensions.InSharedSpace{TBuilder}"/> addresses it, and on such a route the
/// source's compartment is not read. A source therefore cannot open the shared space, even by mistake.
/// </remarks>
public interface IRequestContextSource
{
    /// <summary>
    /// The compartment the request addresses, or <see langword="null"/> when it cannot be resolved — an
    /// endpoint then answers 404. There is no fallback compartment: a source that wants one names it.
    /// </summary>
    Compartment? Compartment { get; }

    /// <summary>
    /// Who performs the request, always present. A module never refuses a request for lack of an actor:
    /// authentication belongs to the application, and an anonymous request carries an anonymous label.
    /// </summary>
    Actor Actor { get; }
}
