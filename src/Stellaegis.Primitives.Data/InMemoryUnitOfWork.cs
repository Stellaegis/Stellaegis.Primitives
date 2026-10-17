namespace Stellaegis.Primitives.Data;

/// <summary>
/// An in-memory unit of work with the same lifecycle as the relational one. There is no real transaction: a
/// rollback restores the repositories that implement <see cref="IRestorableRepository"/>, and leaves the
/// others as they are.
/// </summary>
internal sealed class InMemoryUnitOfWork<TDatabase>(
    InMemoryRepositoryRegistry<TDatabase> registry,
    IEventPublisher publisher) : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];
    private readonly Dictionary<IRestorableRepository, object> _snapshots = new(ReferenceEqualityComparer.Instance);
    private readonly PendingEvents _events = new();

    private bool _hasActiveTransaction;
    private bool _disposed;

    public bool HasActiveTransaction => _hasActiveTransaction;

    public Task BeginAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_hasActiveTransaction)
            throw new InvalidOperationException("A transaction is already active.");

        _hasActiveTransaction = true;
        foreach (var repository in _repositories.Values)
            TakeSnapshot(repository);
        return Task.CompletedTask;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_hasActiveTransaction)
            throw new InvalidOperationException("No active transaction to commit.");

        _hasActiveTransaction = false;
        _snapshots.Clear();
        await _events.PublishAsync(publisher, cancellationToken).ConfigureAwait(false);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_hasActiveTransaction)
            throw new InvalidOperationException("No active transaction to roll back.");

        Undo();
        return Task.CompletedTask;
    }

    public TRepo Repo<TRepo>() where TRepo : class
    {
        ThrowIfDisposed();

        if (_repositories.TryGetValue(typeof(TRepo), out var cached))
            return (TRepo)cached;

        var repository = registry.Resolve(typeof(TRepo))();
        _repositories[typeof(TRepo)] = repository;
        if (_hasActiveTransaction)
            TakeSnapshot(repository);
        return (TRepo)repository;
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : IEvent
    {
        ThrowIfDisposed();
        if (!_hasActiveTransaction)
            throw new InvalidOperationException(
                "No active transaction: publish through IEventPublisher directly outside a transaction.");

        _events.Add(@event);
    }

    /// <summary>Rolls back an active transaction, as the relational unit of work does, then drops the repository cache.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc cref="DisposeAsync"/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hasActiveTransaction)
            Undo();
        _repositories.Clear();
    }

    private void TakeSnapshot(object repository)
    {
        // Several contracts may resolve to one instance: the first snapshot of the transaction is the one kept.
        if (repository is IRestorableRepository restorable && !_snapshots.ContainsKey(restorable))
            _snapshots[restorable] = restorable.Snapshot();
    }

    private void Undo()
    {
        _hasActiveTransaction = false;
        _events.Discard();
        foreach (var (repository, snapshot) in _snapshots)
            repository.Restore(snapshot);
        _snapshots.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
