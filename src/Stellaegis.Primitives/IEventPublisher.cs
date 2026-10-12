namespace Stellaegis.Primitives;

/// <summary>
/// Port through which a module emits its events. The host decides where they go;
/// <see cref="NullEventPublisher"/> discards them.
/// </summary>
/// <remarks>
/// Publication is best effort: the committed state is the source of truth, and an event only shortens the
/// time subscribers take to see it. Call it after the write has been committed — or, inside a unit of work,
/// let the unit publish on commit.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>Publishes <paramref name="event"/>.</summary>
    /// <typeparam name="TEvent">The event type, from which the host resolves the name on the wire.</typeparam>
    /// <remarks>
    /// Implementations do not throw on a transport failure: they log it and return. Callers therefore need
    /// no <c>try/catch</c> around a publication.
    /// </remarks>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
