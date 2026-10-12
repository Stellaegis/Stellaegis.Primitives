using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

public class RegistryTests
{
    [Fact]
    public void Relational_registry_lists_chains_and_refuses_duplicates()
    {
        var registry = SqliteTestDatabase.Registry();

        Assert.True(registry.Contains<IWidgetRepository>());
        Assert.Equal([typeof(IWidgetRepository)], registry.RegisteredTypes);
        Assert.Throws<InvalidOperationException>(() =>
            registry.Add<IWidgetRepository>((connection, transaction) => new SqlWidgetRepository(connection, transaction)));
        Assert.Throws<ArgumentNullException>(() => registry.Add<IDisposable>(null!));
    }

    [Fact]
    public void InMemory_registry_lists_chains_and_refuses_duplicates()
    {
        var registry = new InMemoryRepositoryRegistry<WidgetsDatabase>();

        Assert.Same(registry, registry.Add<IWidgetRepository>(() => new InMemoryWidgetRepository()));
        Assert.True(registry.Contains<IWidgetRepository>());
        Assert.False(registry.Contains<IDisposable>());
        Assert.Equal([typeof(IWidgetRepository)], registry.RegisteredTypes);
        Assert.Throws<InvalidOperationException>(() => registry.Add<IWidgetRepository>(() => new InMemoryWidgetRepository()));
    }
}
