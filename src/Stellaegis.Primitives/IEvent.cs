namespace Stellaegis.Primitives;

/// <summary>
/// Marker for a fact that a module emits once it has happened.
/// </summary>
/// <remarks>
/// An event is data: what a subscriber needs is a field of the event. Routing — the name on the wire and
/// who receives it — is declared by the host when it registers the event type, never by the event itself.
/// </remarks>
public interface IEvent
{
    /// <summary>When the fact occurred. Only the emitter knows it.</summary>
    DateTimeOffset OccurredAt { get; }
}
