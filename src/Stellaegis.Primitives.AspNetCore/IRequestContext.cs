namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The space and the actor of the current HTTP request. A module's endpoints receive it by injection; the
/// application provides the <see cref="IRequestContextSource"/> it is read from, with
/// <c>AddStellaegisRequestContext</c> (<see cref="RequestContextServiceCollectionExtensions"/>).
/// </summary>
/// <remarks>
/// <para>
/// This package provides the only implementation. On a route the application marks with
/// <see cref="SharedSpaceEndpointExtensions.InSharedSpace{TBuilder}"/>, the space is <see cref="Space.Shared"/>;
/// elsewhere it is the compartment of the source, or <see langword="null"/>.
/// </para>
/// <para>
/// The port serves a module's endpoints and filters only. Its core takes the space and the actor as explicit
/// parameters: a singleton receiving this port would keep the context of the first request, and work done
/// outside a request has no context at all.
/// </para>
/// </remarks>
public interface IRequestContext
{
    /// <summary>
    /// Where the request acts: a compartment, or the shared space on a route marked for it. It is
    /// <see langword="null"/> when the compartment cannot be resolved — an endpoint then answers 404. An
    /// unresolved compartment never becomes the shared space.
    /// </summary>
    Space? Space { get; }

    /// <summary>
    /// Who performs the request, always present. A module never refuses a request for lack of an actor:
    /// authentication belongs to the application, and an anonymous request carries an anonymous label.
    /// </summary>
    Actor Actor { get; }
}
