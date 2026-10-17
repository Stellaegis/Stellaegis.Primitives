using System.Data;
using System.Data.Common;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// A unit of work over any ADO.NET provider: it owns a <see cref="DbConnection"/> and an optional
/// <see cref="DbTransaction"/>, and resolves repositories from the registry of its database.
/// </summary>
internal sealed class AdoUnitOfWork<TDatabase>(
    DbConnection connection,
    RepositoryRegistry<TDatabase> registry,
    IEventPublisher publisher,
    IsolationLevel isolationLevel) : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];
    private readonly PendingEvents _events = new();

    private DbTransaction? _transaction;
    private bool _disposed;

    public bool HasActiveTransaction => _transaction is not null;

    /// <summary>
    /// Opens a unit of work on <paramref name="connection"/>, which it then owns: checks the connection before
    /// opening it, opens it, prepares the session — its context, for instance — and begins a transaction if
    /// asked. On any failure the unit is disposed, and the connection with it.
    /// </summary>
    internal static async Task<IUnitOfWork> OpenAsync(
        DbConnection connection,
        RepositoryRegistry<TDatabase> registry,
        IEventPublisher publisher,
        IsolationLevel isolationLevel,
        bool beginTransaction,
        Func<DbConnection, CancellationToken, Task>? prepare,
        Action<DbConnection>? beforeOpen,
        CancellationToken cancellationToken)
    {
        var uow = new AdoUnitOfWork<TDatabase>(connection, registry, publisher, isolationLevel);
        try
        {
            beforeOpen?.Invoke(connection);
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (prepare is not null)
                await prepare(connection, cancellationToken).ConfigureAwait(false);
            if (beginTransaction)
                await uow.BeginAsync(cancellationToken).ConfigureAwait(false);
            return uow;
        }
        catch
        {
            await uow.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_transaction is not null)
            throw new InvalidOperationException("A transaction is already active.");

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        _transaction = isolationLevel == IsolationLevel.Unspecified
            ? await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var transaction = _transaction ?? throw new InvalidOperationException("No active transaction to commit.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;

        await _events.PublishAsync(publisher, cancellationToken).ConfigureAwait(false);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var transaction = _transaction ?? throw new InvalidOperationException("No active transaction to roll back.");

        _events.Discard();
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public TRepo Repo<TRepo>() where TRepo : class
    {
        ThrowIfDisposed();
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("The connection is not open.");

        if (_repositories.TryGetValue(typeof(TRepo), out var cached))
            return (TRepo)cached;

        var repository = registry.Resolve(typeof(TRepo))(connection, () => _transaction);
        _repositories[typeof(TRepo)] = repository;
        return (TRepo)repository;
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        ThrowIfDisposed();
        if (_transaction is null)
            throw new InvalidOperationException(
                "No active transaction: publish through IEventPublisher directly outside a transaction.");

        _events.Add(@event);
    }

    /// <summary>Rolls back an active transaction (best effort), discards its events and disposes the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _events.Discard();

        if (_transaction is not null)
        {
            try { await _transaction.RollbackAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException)
            {
                // Best effort: the connection may be broken, or the transaction already completed.
            }
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        await connection.DisposeAsync().ConfigureAwait(false);
        _repositories.Clear();
    }

    /// <summary>The synchronous counterpart of <see cref="DisposeAsync"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _events.Discard();

        if (_transaction is not null)
        {
            try { _transaction.Rollback(); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException)
            {
                // Best effort: the connection may be broken, or the transaction already completed.
            }
            _transaction.Dispose();
            _transaction = null;
        }

        connection.Dispose();
        _repositories.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
