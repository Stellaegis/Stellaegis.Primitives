using System.Data;
using System.Data.Common;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// Opens units of work over ADO.NET on the database designated by <typeparamref name="TDatabase"/>. Each unit
/// owns the connection it opens and disposes it. A plain object, with no dependency on a DI container.
/// </summary>
/// <typeparam name="TDatabase">The marker type of the database.</typeparam>
/// <param name="connectionProvider">
/// Produces the connection of each unit of work, open or not. Called once per unit, so it may choose the
/// database at that moment — from an ambient context, for instance.
/// </param>
/// <param name="registry">The repository factories of this database.</param>
/// <param name="publisher">Receives the events of each committed transaction. Defaults to <see cref="NullEventPublisher"/>.</param>
/// <param name="isolationLevel">Isolation level of the transactions. <see cref="IsolationLevel.Unspecified"/> lets the provider choose.</param>
public sealed class AdoUnitOfWorkFactory<TDatabase>(
    Func<CancellationToken, Task<DbConnection>> connectionProvider,
    RepositoryRegistry<TDatabase> registry,
    IEventPublisher? publisher = null,
    IsolationLevel isolationLevel = IsolationLevel.Unspecified) : IUnitOfWorkFactory<TDatabase>
{
    private readonly Func<CancellationToken, Task<DbConnection>> _connectionProvider = Guard.NotNull(connectionProvider);
    private readonly RepositoryRegistry<TDatabase> _registry = Guard.NotNull(registry);
    private readonly IEventPublisher _publisher = publisher ?? NullEventPublisher.Instance;

    /// <summary>Opens units of work from <paramref name="dataSource"/>, which designates one fixed database.</summary>
    /// <param name="dataSource">The data source of the database — an <c>NpgsqlDataSource</c>, for instance.</param>
    /// <param name="registry">The repository factories of this database.</param>
    /// <param name="publisher">Receives the events of each committed transaction. Defaults to <see cref="NullEventPublisher"/>.</param>
    /// <param name="isolationLevel">Isolation level of the transactions.</param>
    public AdoUnitOfWorkFactory(
        DbDataSource dataSource,
        RepositoryRegistry<TDatabase> registry,
        IEventPublisher? publisher = null,
        IsolationLevel isolationLevel = IsolationLevel.Unspecified)
        : this(OpenFrom(Guard.NotNull(dataSource)), registry, publisher, isolationLevel)
    {
    }

    /// <inheritdoc/>
    public Task<IUnitOfWork> OpenAsync(CancellationToken cancellationToken = default) =>
        OpenCoreAsync(beginTransaction: false, cancellationToken);

    /// <inheritdoc/>
    public Task<IUnitOfWork> OpenAndBeginAsync(CancellationToken cancellationToken = default) =>
        OpenCoreAsync(beginTransaction: true, cancellationToken);

    private async Task<IUnitOfWork> OpenCoreAsync(bool beginTransaction, CancellationToken cancellationToken)
    {
        var connection = await _connectionProvider(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The connection provider returned null.");

        return await AdoUnitOfWork<TDatabase>.OpenAsync(
            connection, _registry, _publisher, isolationLevel, beginTransaction,
            prepare: null, beforeOpen: null, cancellationToken).ConfigureAwait(false);
    }

    private static Func<CancellationToken, Task<DbConnection>> OpenFrom(DbDataSource dataSource) =>
        ct => dataSource.OpenConnectionAsync(ct).AsTask();
}
