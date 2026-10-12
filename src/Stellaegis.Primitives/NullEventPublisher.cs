namespace Stellaegis.Primitives;

/// <summary>
/// <see cref="IEventPublisher"/> that discards every event: the default for a module hosted without a bus.
/// </summary>
public sealed class NullEventPublisher : IEventPublisher
{
    /// <summary>The shared instance.</summary>
    public static readonly NullEventPublisher Instance = new();

    private NullEventPublisher()
    {
    }

    /// <inheritdoc/>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
        => Task.CompletedTask;
}
