namespace Stellaegis.Primitives.Data;

/// <summary>
/// Opens in-memory units of work standing in for the database designated by <typeparamref name="TDatabase"/>.
/// Swap it in for <see cref="AdoUnitOfWorkFactory{TDatabase}"/> at the composition root — for tests or demos —
/// without touching the code that depends on <see cref="IUnitOfWorkFactory{TDatabase}"/>.
/// </summary>
/// <typeparam name="TDatabase">The marker type of the database.</typeparam>
/// <param name="registry">The in-memory repository factories of this database.</param>
/// <param name="publisher">Receives the events of each committed transaction. Defaults to <see cref="NullEventPublisher"/>.</param>
public sealed class InMemoryUnitOfWorkFactory<TDatabase>(
    InMemoryRepositoryRegistry<TDatabase> registry,
    IEventPublisher? publisher = null) : IUnitOfWorkFactory<TDatabase>
{
    private readonly InMemoryRepositoryRegistry<TDatabase> _registry = Guard.NotNull(registry);
    private readonly IEventPublisher _publisher = publisher ?? NullEventPublisher.Instance;

    /// <inheritdoc/>
    public Task<IUnitOfWork> OpenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IUnitOfWork>(new InMemoryUnitOfWork<TDatabase>(_registry, _publisher));

    /// <inheritdoc/>
    public async Task<IUnitOfWork> OpenAndBeginAsync(CancellationToken cancellationToken = default)
    {
        var uow = new InMemoryUnitOfWork<TDatabase>(_registry, _publisher);
        await uow.BeginAsync(cancellationToken).ConfigureAwait(false);
        return uow;
    }
}
