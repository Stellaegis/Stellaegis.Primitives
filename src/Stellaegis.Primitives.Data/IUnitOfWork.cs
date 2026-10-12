namespace Stellaegis.Primitives.Data;

/// <summary>
/// A transactional unit of work, opened per operation by an <see cref="IUnitOfWorkFactory{TDatabase}"/>. It
/// hands out repositories "à la carte" through <see cref="Repo{TRepo}"/> and publishes the events of a
/// transaction once it commits.
/// </summary>
/// <remarks>
/// The <b>type</b> returned by <see cref="Repo{TRepo}"/> is checked at compile time; the <b>availability</b>
/// of a repository (whether a factory was registered for it) is checked at run time, and an unregistered
/// repository fails fast with <see cref="RepositoryNotRegisteredException"/>. Disposing a unit of work with
/// an active transaction rolls it back.
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Whether a transaction is currently active.</summary>
    bool HasActiveTransaction { get; }

    /// <summary>Begins a transaction.</summary>
    /// <exception cref="InvalidOperationException">A transaction is already active.</exception>
    Task BeginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the active transaction, then hands the events published during it to the
    /// <see cref="IEventPublisher"/>, in publication order.
    /// </summary>
    /// <exception cref="InvalidOperationException">No transaction is active.</exception>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls the active transaction back and discards the events published during it.</summary>
    /// <exception cref="InvalidOperationException">No transaction is active.</exception>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the repository of type <typeparamref name="TRepo"/>, built on demand from its registered
    /// factory and cached for the lifetime of this unit of work. A cached repository always observes the
    /// live transaction — including none, or several across begin/commit cycles.
    /// </summary>
    /// <typeparam name="TRepo">The repository contract to resolve.</typeparam>
    /// <exception cref="RepositoryNotRegisteredException">No factory is registered for <typeparamref name="TRepo"/>.</exception>
    TRepo Repo<TRepo>() where TRepo : class;

    /// <summary>
    /// Holds <paramref name="event"/> until the active transaction ends: it is published after a successful
    /// commit, and discarded on rollback or when the unit of work is disposed without committing.
    /// </summary>
    /// <typeparam name="TEvent">The event type, passed on to <see cref="IEventPublisher.PublishAsync{TEvent}"/>.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// No transaction is active. Outside a transaction, publish through <see cref="IEventPublisher"/> directly.
    /// </exception>
    void Publish<TEvent>(TEvent @event) where TEvent : IEvent;
}
