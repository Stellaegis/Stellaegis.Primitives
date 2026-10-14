namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The compartment and the actor of the current HTTP request. A module's endpoints receive it by injection;
/// the consumer provides the adapter that reads them, with
/// <c>AddStellaegisRequestContext</c> (<see cref="RequestContextServiceCollectionExtensions"/>).
/// </summary>
/// <remarks>
/// The port serves a module's endpoints and filters only. Its core takes the compartment and the actor as
/// explicit parameters: a singleton receiving this port would keep the context of the first request, and
/// work done outside a request has no context at all.
/// </remarks>
public interface IRequestContext
{
    /// <summary>
    /// The compartment the request addresses, or <see langword="null"/> when it cannot be resolved — an
    /// endpoint then answers 404. There is no fallback compartment: an adapter that wants one names it.
    /// </summary>
    Compartment? Compartment { get; }

    /// <summary>
    /// Who performs the request, always present. A module never refuses a request for lack of an actor:
    /// authentication belongs to the consumer, and an anonymous request carries an anonymous label.
    /// </summary>
    Actor Actor { get; }
}
