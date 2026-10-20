using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>
/// Presents a factory opening in a space as a plain one, always in the same space, so that the whole contract of a
/// unit of work runs against it.
/// </summary>
internal sealed class InFixedSpace<TDatabase>(ISpaceUnitOfWorkFactory<TDatabase> factory, Space space, Actor actor)
    : IUnitOfWorkFactory<TDatabase>
{
    public Task<IUnitOfWork> OpenAsync(CancellationToken cancellationToken = default) =>
        factory.OpenAsync(space, actor, cancellationToken);

    public Task<IUnitOfWork> OpenAndBeginAsync(CancellationToken cancellationToken = default) =>
        factory.OpenAndBeginAsync(space, actor, cancellationToken);
}

public sealed class AdoSpaceUnitOfWorkContractTests : UnitOfWorkContractTests
{
    private SqliteTestDatabase _db = null!;
    private IUnitOfWorkFactory<WidgetsDatabase> _factory = null!;

    protected override IUnitOfWorkFactory<WidgetsDatabase> Factory => _factory;

    public override async ValueTask InitializeAsync()
    {
        _db = await SqliteTestDatabase.CreateAsync();
        var spaced = new AdoSpaceUnitOfWorkFactory<WidgetsDatabase>(
            (_, ct) => _db.ProvideConnectionAsync(ct), SqliteTestDatabase.Registry(), SessionContext.None, Publisher);
        _factory = new InFixedSpace<WidgetsDatabase>(spaced, Compartment.Parse("acme"), Actor.Parse("jane"));
    }

    public override async ValueTask DisposeAsync() => await _db.DisposeAsync();
}

public sealed class AdoSpaceUnitOfWorkTests : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Actor Jane = Actor.Parse("jane");

    private SqliteTestDatabase _db = null!;

    public async ValueTask InitializeAsync() => _db = await SqliteTestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task The_connection_is_chosen_for_the_space()
    {
        var asked = new List<Space>();
        var factory = new AdoSpaceUnitOfWorkFactory<WidgetsDatabase>(
            (space, ct) => { asked.Add(space); return _db.ProvideConnectionAsync(ct); },
            SqliteTestDatabase.Registry(), SessionContext.None);

        await using (await factory.OpenAsync(Compartment.Parse("acme"), Jane, Ct)) { }
        await using (await factory.OpenAndBeginAsync(Space.Shared, Jane, Ct)) { }

        Assert.Equal([Space.Of(Compartment.Parse("acme")), Space.Shared], asked);
    }

    [Fact]
    public async Task A_space_and_an_actor_are_required()
    {
        var factory = new AdoSpaceUnitOfWorkFactory<WidgetsDatabase>(
            (_, ct) => _db.ProvideConnectionAsync(ct), SqliteTestDatabase.Registry(), SessionContext.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.OpenAsync(null!, Jane, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.OpenAsync(Space.Shared, null!, Ct));
    }

    [Fact]
    public async Task A_data_source_shares_one_database_between_spaces()
    {
        await using var dataSource = Microsoft.Data.Sqlite.SqliteFactory.Instance.CreateDataSource(_db.ConnectionString);
        var factory = new AdoSpaceUnitOfWorkFactory<WidgetsDatabase>(dataSource, SqliteTestDatabase.Registry(), SessionContext.None);

        await using (var uow = await factory.OpenAndBeginAsync(Compartment.Parse("acme"), Jane, Ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", Ct);
            await uow.CommitAsync(Ct);
        }

        Assert.Equal(1, await _db.CountWidgetsAsync());
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Username=postgres;No Reset On Close=true")]
    [InlineData("Host=127.0.0.1;Username=postgres;NoResetOnClose=True")]
    public async Task PostgreSql_refuses_a_connection_that_would_not_be_reset(string connectionString)
    {
        var factory = new AdoSpaceUnitOfWorkFactory<WidgetsDatabase>(
            (_, _) => Task.FromResult<DbConnection>(new NpgsqlConnection(connectionString)),
            new RepositoryRegistry<WidgetsDatabase>(), SessionContext.PostgreSql);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenAsync(Space.Shared, Jane, Ct));
        Assert.Contains("No Reset On Close", exception.Message);
    }
}

/// <summary>Reads the session context back, as a row-level security policy or an audit trigger would.</summary>
public interface ISessionProbe
{
    Task<(string? Space, string? Actor)> ReadAsync(CancellationToken cancellationToken = default);

    Task<string?> ReadViaAsync(CancellationToken cancellationToken = default);

    Task SetSpaceAsync(string value, CancellationToken cancellationToken = default);
}

internal sealed class SqlSessionProbe(DbConnection connection, Func<DbTransaction?> transaction, string readSql, string setSql, string readViaSql)
    : ISessionProbe
{
    public async Task<string?> ReadViaAsync(CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction();
        command.CommandText = readViaSql;
        return await command.ExecuteScalarAsync(cancellationToken) is string via ? via : null;
    }

    public async Task<(string? Space, string? Actor)> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction();
        command.CommandText = readSql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    public async Task SetSpaceAsync(string value, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction();
        command.CommandText = setSql.Replace("{value}", value, StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// The session context on real engines, named by <c>STELLAEGIS_TEST_POSTGRESQL</c> and
/// <c>STELLAEGIS_TEST_SQLSERVER</c>, skipped without them.
/// </summary>
public sealed class SessionContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string PostgreSqlRead =
        "select nullif(current_setting('stellaegis.space', true), ''), nullif(current_setting('stellaegis.actor', true), '')";

    private const string SqlServerRead =
        "select cast(session_context(N'stellaegis.space') as nvarchar(63)), cast(session_context(N'stellaegis.actor') as nvarchar(128))";

    private const string PostgreSqlReadVia = "select nullif(current_setting('stellaegis.actor_via', true), '')";

    private const string SqlServerReadVia = "select cast(session_context(N'stellaegis.actor_via') as nvarchar(128))";

    private static string ConnectionString(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), $"{variable} is not set.");
        return value!;
    }

    private static AdoSpaceUnitOfWorkFactory<WidgetsDatabase> PostgreSql() => new(
        (_, _) => Task.FromResult<DbConnection>(new NpgsqlConnection(ConnectionString("STELLAEGIS_TEST_POSTGRESQL"))),
        new RepositoryRegistry<WidgetsDatabase>().Add<ISessionProbe>((connection, transaction) =>
            new SqlSessionProbe(connection, transaction, PostgreSqlRead, "select set_config('stellaegis.space', '{value}', false)", PostgreSqlReadVia)),
        SessionContext.PostgreSql);

    private static AdoSpaceUnitOfWorkFactory<WidgetsDatabase> SqlServer() => new(
        (_, _) => Task.FromResult<DbConnection>(new SqlConnection(ConnectionString("STELLAEGIS_TEST_SQLSERVER"))),
        new RepositoryRegistry<WidgetsDatabase>().Add<ISessionProbe>((connection, transaction) =>
            new SqlSessionProbe(connection, transaction, SqlServerRead, "exec sp_set_session_context N'stellaegis.space', N'{value}'", SqlServerReadVia)),
        SessionContext.SqlServer);

    public static TheoryData<string> Engines => ["PostgreSql", "SqlServer"];

    private static AdoSpaceUnitOfWorkFactory<WidgetsDatabase> Factory(string engine)
        => engine == "PostgreSql" ? PostgreSql() : SqlServer();

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task The_database_reads_the_space_and_the_actor_without_a_transaction(string engine)
    {
        await using var uow = await Factory(engine).OpenAsync(Compartment.Parse("acme"), Actor.Parse("jane"), Ct);

        Assert.Equal(("acme", "jane"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task The_context_holds_across_successive_transactions(string engine)
    {
        await using var uow = await Factory(engine).OpenAndBeginAsync(Space.Shared, Actor.Parse("job:trash-purge"), Ct);
        Assert.Equal(("_shared", "job:trash-purge"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));
        await uow.CommitAsync(Ct);

        Assert.Equal(("_shared", "job:trash-purge"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));

        await uow.BeginAsync(Ct);
        Assert.Equal(("_shared", "job:trash-purge"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));
        await uow.RollbackAsync(Ct);
    }

    // The acting party reaches the database only when there is one; a pooled connection does not keep it.
    [Theory]
    [MemberData(nameof(Engines))]
    public async Task The_database_reads_who_acts_on_the_actors_behalf(string engine)
    {
        var factory = Factory(engine);
        await using (var uow = await factory.OpenAsync(Compartment.Parse("acme"), Actor.Parse("jane").Through(Actor.Job("provision")), Ct))
        {
            Assert.Equal(("acme", "jane"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));
            Assert.Equal("job:provision", await uow.Repo<ISessionProbe>().ReadViaAsync(Ct));
        }

        await using (var uow = await factory.OpenAsync(Compartment.Parse("acme"), Actor.Parse("jane"), Ct))
        {
            Assert.Null(await uow.Repo<ISessionProbe>().ReadViaAsync(Ct));
        }
    }

    [Fact]
    public async Task SqlServer_forbids_changing_the_space_inside_a_unit_of_work()
    {
        await using var uow = await SqlServer().OpenAsync(Compartment.Parse("acme"), Actor.Parse("jane"), Ct);

        await Assert.ThrowsAsync<SqlException>(() => uow.Repo<ISessionProbe>().SetSpaceAsync("globex", Ct));
        Assert.Equal(("acme", "jane"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task The_next_unit_of_work_on_a_pooled_connection_sees_its_own_context(string engine)
    {
        var factory = Factory(engine);
        await using (await factory.OpenAsync(Compartment.Parse("acme"), Actor.Parse("jane"), Ct)) { }

        await using var uow = await factory.OpenAsync(Compartment.Parse("globex"), Actor.Parse("paul"), Ct);
        Assert.Equal(("globex", "paul"), await uow.Repo<ISessionProbe>().ReadAsync(Ct));
    }
}

public sealed class InMemorySpaceUnitOfWorkContractTests : UnitOfWorkContractTests
{
    private readonly InMemoryWidgetRepository _store = new();

    protected override IUnitOfWorkFactory<WidgetsDatabase> Factory => new InFixedSpace<WidgetsDatabase>(
        new InMemorySpaceUnitOfWorkFactory<WidgetsDatabase>(
            (_, _) => new InMemoryRepositoryRegistry<WidgetsDatabase>().Add<IWidgetRepository>(() => _store), Publisher),
        Compartment.Parse("acme"),
        Actor.Parse("jane"));
}

public sealed class InMemorySpaceUnitOfWorkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_space_has_the_store_its_registry_gives()
    {
        var stores = new Dictionary<Space, InMemoryWidgetRepository>();
        var seen = new List<(Space, Actor)>();
        var factory = new InMemorySpaceUnitOfWorkFactory<WidgetsDatabase>((space, actor) =>
        {
            seen.Add((space, actor));
            var store = stores.TryGetValue(space, out var existing) ? existing : stores[space] = new InMemoryWidgetRepository();
            return new InMemoryRepositoryRegistry<WidgetsDatabase>().Add<IWidgetRepository>(() => store);
        });

        await using (var uow = await factory.OpenAndBeginAsync(Compartment.Parse("acme"), Actor.Parse("jane"), Ct))
        {
            await uow.Repo<IWidgetRepository>().InsertAsync("a", Ct);
            await uow.CommitAsync(Ct);
        }

        await using var other = await factory.OpenAsync(Compartment.Parse("globex"), Actor.Parse("paul"), Ct);
        Assert.Equal(0, await other.Repo<IWidgetRepository>().CountAsync(Ct));
        Assert.Equal(
            [(Space.Of(Compartment.Parse("acme")), Actor.Parse("jane")), (Space.Of(Compartment.Parse("globex")), Actor.Parse("paul"))],
            seen);
    }

    [Fact]
    public async Task A_space_and_an_actor_are_required()
    {
        var factory = new InMemorySpaceUnitOfWorkFactory<WidgetsDatabase>((_, _) => new InMemoryRepositoryRegistry<WidgetsDatabase>());

        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.OpenAsync(null!, Actor.Parse("jane"), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => factory.OpenAsync(Space.Shared, null!, Ct));
    }
}
