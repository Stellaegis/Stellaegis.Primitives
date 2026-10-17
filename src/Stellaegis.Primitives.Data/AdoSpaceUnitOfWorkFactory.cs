using System.Data;
using System.Data.Common;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// Opens units of work over ADO.NET in a space, on the database designated by <typeparamref name="TDatabase"/>,
/// and sets the space and the actor in each connection's session context. A plain object, with no dependency on
/// a DI container.
/// </summary>
/// <typeparam name="TDatabase">The marker type of the database.</typeparam>
/// <param name="connectionProvider">
/// Produces the connection of each unit of work for its space, open or not — one database per compartment, for
/// instance. Called once per unit.
/// </param>
/// <param name="registry">The repository factories of this database.</param>
/// <param name="sessionContext">How the engine receives the space and the actor: <see cref="SessionContext.SqlServer"/>,
/// <see cref="SessionContext.PostgreSql"/>, or <see cref="SessionContext.None"/>.</param>
/// <param name="publisher">Receives the events of each committed transaction. Defaults to <see cref="NullEventPublisher"/>.</param>
/// <param name="isolationLevel">Isolation level of the transactions. <see cref="IsolationLevel.Unspecified"/> lets the provider choose.</param>
public sealed class AdoSpaceUnitOfWorkFactory<TDatabase>(
    Func<Space, CancellationToken, Task<DbConnection>> connectionProvider,
    RepositoryRegistry<TDatabase> registry,
    SessionContext sessionContext,
    IEventPublisher? publisher = null,
    IsolationLevel isolationLevel = IsolationLevel.Unspecified) : ISpaceUnitOfWorkFactory<TDatabase>
{
    private readonly Func<Space, CancellationToken, Task<DbConnection>> _connectionProvider = Guard.NotNull(connectionProvider);
    private readonly RepositoryRegistry<TDatabase> _registry = Guard.NotNull(registry);
    private readonly SessionContext _sessionContext = Guard.NotNull(sessionContext);
    private readonly IEventPublisher _publisher = publisher ?? NullEventPublisher.Instance;

    /// <summary>
    /// Opens units of work from <paramref name="dataSource"/>, one database shared by every space — the space
    /// then lives in a column of each table, and the session context tells the database which.
    /// </summary>
    /// <param name="dataSource">The data source of the database — an <c>NpgsqlDataSource</c>, for instance.</param>
    /// <param name="registry">The repository factories of this database.</param>
    /// <param name="sessionContext">How the engine receives the space and the actor.</param>
    /// <param name="publisher">Receives the events of each committed transaction. Defaults to <see cref="NullEventPublisher"/>.</param>
    /// <param name="isolationLevel">Isolation level of the transactions.</param>
    public AdoSpaceUnitOfWorkFactory(
        DbDataSource dataSource,
        RepositoryRegistry<TDatabase> registry,
        SessionContext sessionContext,
        IEventPublisher? publisher = null,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified)
        : this(CreateFrom(Guard.NotNull(dataSource)), registry, sessionContext, publisher, isolationLevel)
    {
    }

    /// <inheritdoc/>
    public Task<IUnitOfWork> OpenAsync(Space space, Actor actor, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(space, actor, beginTransaction: false, cancellationToken);

    /// <inheritdoc/>
    public Task<IUnitOfWork> OpenAndBeginAsync(Space space, Actor actor, CancellationToken cancellationToken = default) =>
        OpenCoreAsync(space, actor, beginTransaction: true, cancellationToken);

    private async Task<IUnitOfWork> OpenCoreAsync(Space space, Actor actor, bool beginTransaction, CancellationToken cancellationToken)
    {
        Guard.NotNull(space);
        Guard.NotNull(actor);

        var connection = await _connectionProvider(space, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The connection provider returned null for the space {space}.");

        return await AdoUnitOfWork<TDatabase>.OpenAsync(
            connection, _registry, _publisher, isolationLevel, beginTransaction,
            async (opened, ct) => await _sessionContext.ApplyAsync(opened, space, actor, ct).ConfigureAwait(false),
            beforeOpen: _sessionContext.EnsureSupports,
            cancellationToken).ConfigureAwait(false);
    }

    private static Func<Space, CancellationToken, Task<DbConnection>> CreateFrom(DbDataSource dataSource) =>
        (_, _) => Task.FromResult(dataSource.CreateConnection());
}
