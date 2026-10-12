namespace Stellaegis.Primitives.Data;

/// <summary>
/// The events held by a unit of work until its transaction ends. Each is kept as a closure so that the
/// publisher receives it with its own type, not as <see cref="IEvent"/>.
/// </summary>
internal sealed class PendingEvents
{
    private readonly List<Func<IEventPublisher, CancellationToken, Task>> _events = [];

    public void Add<TEvent>(TEvent @event) where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events.Add((publisher, ct) => publisher.PublishAsync(@event, ct));
    }

    public void Discard() => _events.Clear();

    /// <summary>Publishes the held events in order, after emptying the list so a later cycle starts clean.</summary>
    public async Task PublishAsync(IEventPublisher publisher, CancellationToken cancellationToken)
    {
        if (_events.Count == 0) return;

        var events = _events.ToArray();
        _events.Clear();
        foreach (var publish in events)
            await publish(publisher, cancellationToken).ConfigureAwait(false);
    }
}
