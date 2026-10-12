using System.Data.Common;
using Microsoft.Data.Sqlite;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>The database marker used by the tests.</summary>
public sealed class WidgetsDatabase;

public interface IWidgetRepository
{
    Task InsertAsync(string name, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

public sealed record WidgetAdded(string Name, DateTimeOffset OccurredAt) : IEvent;

/// <summary>
/// A blind repository over raw ADO.NET: it forwards whatever transaction is current to each command,
/// without knowing whether one is active.
/// </summary>
internal sealed class SqlWidgetRepository(DbConnection connection, Func<DbTransaction?> currentTransaction)
    : IWidgetRepository
{
    public async Task InsertAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = currentTransaction();
        cmd.CommandText = "INSERT INTO widgets (name) VALUES ($name)";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = name;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = currentTransaction();
        cmd.CommandText = "SELECT COUNT(*) FROM widgets";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }
}

/// <summary>An in-memory repository over an immutable list, restorable on rollback.</summary>
internal sealed class InMemoryWidgetRepository : IWidgetRepository, IRestorableRepository
{
    private List<string> _widgets = [];

    public Task InsertAsync(string name, CancellationToken cancellationToken = default)
    {
        _widgets.Add(name);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(_widgets.Count);

    public object Snapshot() => _widgets.ToList();

    public void Restore(object snapshot) => _widgets = (List<string>)snapshot;
}

/// <summary>The same store without <see cref="IRestorableRepository"/>: rollbacks leave it as it is.</summary>
internal sealed class PlainInMemoryWidgetRepository : IWidgetRepository
{
    private readonly List<string> _widgets = [];

    public Task InsertAsync(string name, CancellationToken cancellationToken = default)
    {
        _widgets.Add(name);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(_widgets.Count);
}

public sealed class RecordingPublisher : IEventPublisher
{
    public List<(Type StaticType, IEvent Event)> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        Published.Add((typeof(TEvent), @event));
        return Task.CompletedTask;
    }
}

/// <summary>
/// A private, shared-cache in-memory SQLite database for one test. An anchor connection keeps it alive while
/// the units of work open and dispose their own connections.
/// </summary>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _anchor;

    private SqliteTestDatabase(string connectionString, SqliteConnection anchor)
    {
        ConnectionString = connectionString;
        _anchor = anchor;
    }

    public string ConnectionString { get; }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var connectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        await using var cmd = anchor.CreateCommand();
        cmd.CommandText = "CREATE TABLE widgets (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL)";
        await cmd.ExecuteNonQueryAsync();

        return new SqliteTestDatabase(connectionString, anchor);
    }

    public Task<DbConnection> ProvideConnectionAsync(CancellationToken cancellationToken) =>
        Task.FromResult<DbConnection>(new SqliteConnection(ConnectionString));

    /// <summary>Counts rows through an independent connection.</summary>
    public async Task<int> CountWidgetsAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM widgets";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public static RepositoryRegistry<WidgetsDatabase> Registry() =>
        new RepositoryRegistry<WidgetsDatabase>()
            .Add<IWidgetRepository>((connection, transaction) => new SqlWidgetRepository(connection, transaction));

    public async ValueTask DisposeAsync() => await _anchor.DisposeAsync();
}
