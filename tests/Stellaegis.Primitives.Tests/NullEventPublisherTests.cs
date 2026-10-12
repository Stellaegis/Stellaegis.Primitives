namespace Stellaegis.Primitives.Tests;

public class NullEventPublisherTests
{
    private sealed record SomethingHappened(DateTimeOffset OccurredAt) : IEvent;

    [Fact]
    public async Task Publishing_completes_without_doing_anything()
    {
        IEventPublisher publisher = NullEventPublisher.Instance;

        await publisher.PublishAsync(new SomethingHappened(DateTimeOffset.UtcNow), TestContext.Current.CancellationToken);
    }
}
