using Microsoft.Data.Sqlite;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>
/// The contract every <see cref="IUnitOfWorkFactory{TDatabase}"/> honours, run against the relational and the
/// in-memory implementation alike.
/// </summary>
public abstract class UnitOfWorkContractTests : IAsyncLifetime
{
    protected RecordingPublisher Publisher { get; } = new();

    protected abstract IUnitOfWorkFactory<WidgetsDatabase> Factory { get; }

    /// <summary>Counts the widgets as a later operation would see them.</summary>
    protected async Task<int> CountCommittedAsync()
    {
        await using var uow = await Factory.OpenAsync(TestContext.Current.CancellationToken);
        return await uow.Repo<IWidgetRepository>().CountAsync(TestContext.Current.CancellationToken);
    }

    public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static WidgetAdded Added(string name) => new(name, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Commit_persists_writes_and_publishes_events_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var uow = await Factory.OpenAndBeginAsync(ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);
            uow.Publish(Added("a"));
            await uow.Repo<IWidgetRepository>().InsertAsync("b", ct);
            uow.Publish(Added("b"));

            Assert.Empty(Publisher.Published);
            await uow.CommitAsync(ct);
        }

        Assert.Equal(2, await CountCommittedAsync());
        Assert.Equal(["a", "b"], Publisher.Published.Select(p => ((WidgetAdded)p.Event).Name));
        Assert.All(Publisher.Published, p => Assert.Equal(typeof(WidgetAdded), p.StaticType));
    }

    [Fact]
    public async Task Rollback_discards_writes_and_events()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var uow = await Factory.OpenAndBeginAsync(ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);
            uow.Publish(Added("a"));
            await uow.RollbackAsync(ct);
            Assert.False(uow.HasActiveTransaction);
        }

        Assert.Equal(0, await CountCommittedAsync());
        Assert.Empty(Publisher.Published);
    }

    [Fact]
    public async Task Dispose_without_commit_rolls_back_and_discards_events()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var uow = await Factory.OpenAndBeginAsync(ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);
            uow.Publish(Added("a"));
        }

        Assert.Equal(0, await CountCommittedAsync());
        Assert.Empty(Publisher.Published);
    }

    [Fact]
    public async Task Synchronous_dispose_without_commit_rolls_back_and_discards_events()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var uow = await Factory.OpenAndBeginAsync(ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);
            uow.Publish(Added("a"));
        }

        Assert.Equal(0, await CountCommittedAsync());
        Assert.Empty(Publisher.Published);
    }

    [Fact]
    public async Task Repository_resolved_before_begin_is_rolled_back_too()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var uow = await Factory.OpenAsync(ct))
        {
            var widgets = uow.Repo<IWidgetRepository>();
            await uow.BeginAsync(ct);
            await widgets.InsertAsync("a", ct);
            await uow.RollbackAsync(ct);
        }

        Assert.Equal(0, await CountCommittedAsync());
    }

    [Fact]
    public async Task Each_commit_publishes_only_the_events_of_its_own_transaction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var uow = await Factory.OpenAsync(ct);

        await uow.BeginAsync(ct);
        uow.Publish(Added("first"));
        await uow.CommitAsync(ct);

        await uow.BeginAsync(ct);
        uow.Publish(Added("dropped"));
        await uow.RollbackAsync(ct);

        await uow.BeginAsync(ct);
        uow.Publish(Added("second"));
        await uow.CommitAsync(ct);

        Assert.Equal(["first", "second"], Publisher.Published.Select(p => ((WidgetAdded)p.Event).Name));
    }

    [Fact]
    public async Task Publish_without_a_transaction_is_refused()
    {
        await using var uow = await Factory.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => uow.Publish(Added("a")));
    }

    [Fact]
    public async Task Open_does_not_begin_and_OpenAndBegin_does()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var opened = await Factory.OpenAsync(ct);
        await using var begun = await Factory.OpenAndBeginAsync(ct);

        Assert.False(opened.HasActiveTransaction);
        Assert.True(begun.HasActiveTransaction);
    }

    [Fact]
    public async Task Lifecycle_misuse_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var uow = await Factory.OpenAsync(ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.CommitAsync(ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.RollbackAsync(ct));
        await uow.BeginAsync(ct);
        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.BeginAsync(ct));
    }

    [Fact]
    public async Task Repo_is_cached_and_unregistered_types_fail_fast()
    {
        await using var uow = await Factory.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Same(uow.Repo<IWidgetRepository>(), uow.Repo<IWidgetRepository>());
        var ex = Assert.Throws<RepositoryNotRegisteredException>(() => uow.Repo<IDisposable>());
        Assert.Equal(typeof(IDisposable), ex.RepositoryType);
    }

    [Fact]
    public async Task Disposed_unit_of_work_refuses_further_use()
    {
        var uow = await Factory.OpenAsync(TestContext.Current.CancellationToken);
        await uow.DisposeAsync();
        await uow.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => uow.Repo<IWidgetRepository>());
    }

    [Fact]
    public async Task Synchronous_and_asynchronous_dispose_mix_and_repeat()
    {
        var uow = await Factory.OpenAsync(TestContext.Current.CancellationToken);
        uow.Dispose();
        await uow.DisposeAsync();
        uow.Dispose();

        Assert.Throws<ObjectDisposedException>(() => uow.Repo<IWidgetRepository>());
    }
}

public sealed class AdoUnitOfWorkTests : UnitOfWorkContractTests
{
    private SqliteTestDatabase _db = null!;
    private IUnitOfWorkFactory<WidgetsDatabase> _factory = null!;

    protected override IUnitOfWorkFactory<WidgetsDatabase> Factory => _factory;

    public override async ValueTask InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        _factory = new AdoUnitOfWorkFactory<WidgetsDatabase>(_db.ProvideConnectionAsync, SqliteTestDatabase.Registry(), Publisher);
    }

    public override async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Writes_are_invisible_outside_until_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var uow = await Factory.OpenAndBeginAsync(ct);
        await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);

        await uow.CommitAsync(ct);

        Assert.Equal(1, await _db.CountWidgetsAsync());
    }

    [Fact]
    public async Task Data_source_constructor_opens_from_the_data_source()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var dataSource = SqliteFactory.Instance.CreateDataSource(_db.ConnectionString);
        var factory = new AdoUnitOfWorkFactory<WidgetsDatabase>(dataSource, SqliteTestDatabase.Registry());

        await using (var uow = await factory.OpenAndBeginAsync(ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);
            await uow.CommitAsync(ct);
        }

        Assert.Equal(1, await _db.CountWidgetsAsync());
    }
}

public sealed class InMemoryUnitOfWorkTests : UnitOfWorkContractTests
{
    private readonly InMemoryWidgetRepository _store = new();

    protected override IUnitOfWorkFactory<WidgetsDatabase> Factory => new InMemoryUnitOfWorkFactory<WidgetsDatabase>(
        new InMemoryRepositoryRegistry<WidgetsDatabase>().Add<IWidgetRepository>(() => _store), Publisher);

    [Fact]
    public async Task A_repository_that_is_not_restorable_keeps_its_writes_on_rollback()
    {
        var ct = TestContext.Current.CancellationToken;
        var plain = new PlainInMemoryWidgetRepository();
        var factory = new InMemoryUnitOfWorkFactory<WidgetsDatabase>(
            new InMemoryRepositoryRegistry<WidgetsDatabase>().Add<IWidgetRepository>(() => plain));

        await using (var uow = await factory.OpenAndBeginAsync(ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", ct);
            await uow.RollbackAsync(ct);
        }

        Assert.Equal(1, await plain.CountAsync(ct));
    }
}
