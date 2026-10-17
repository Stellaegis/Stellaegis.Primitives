namespace Stellaegis.Primitives.Data;

/// <summary>
/// Opens in-memory units of work in a space, standing in for the database designated by
/// <typeparamref name="TDatabase"/>. Swap it in for <see cref="AdoSpaceUnitOfWorkFactory{TDatabase}"/> at the
/// composition root — for tests or demos — without touching the code that depends on
/// <see cref="ISpaceUnitOfWorkFactory{TDatabase}"/>.
/// </summary>
/// <typeparam name="TDatabase">The marker type of the database.</typeparam>
/// <param name="registryFor">
/// The repositories of a unit of work opened in a space for an actor — what the session context gives a real
/// database: one store per compartment, or one store whose repositories filter by the space and the actor they
/// were built for. Called once per unit.
/// </param>
/// <param name="publisher">Receives the events of each committed transaction. Defaults to <see cref="NullEventPublisher"/>.</param>
public sealed class InMemorySpaceUnitOfWorkFactory<TDatabase>(
    Func<Space, Actor, InMemoryRepositoryRegistry<TDatabase>> registryFor,
    IEventPublisher? publisher = null) : ISpaceUnitOfWorkFactory<TDatabase>
{
    private readonly Func<Space, Actor, InMemoryRepositoryRegistry<TDatabase>> _registryFor = Guard.NotNull(registryFor);
    private readonly IEventPublisher _publisher = publisher ?? NullEventPublisher.Instance;

    /// <inheritdoc/>
    public Task<IUnitOfWork> OpenAsync(Space space, Actor actor, CancellationToken cancellationToken = default) =>
        Task.FromResult<IUnitOfWork>(Create(space, actor));

    /// <inheritdoc/>
    public async Task<IUnitOfWork> OpenAndBeginAsync(Space space, Actor actor, CancellationToken cancellationToken = default)
    {
        var uow = Create(space, actor);
        await uow.BeginAsync(cancellationToken).ConfigureAwait(false);
        return uow;
    }

    private InMemoryUnitOfWork<TDatabase> Create(Space space, Actor actor)
    {
        Guard.NotNull(space);
        Guard.NotNull(actor);
        var registry = _registryFor(space, actor)
            ?? throw new InvalidOperationException($"No in-memory repositories were given for the space {space}.");
        return new InMemoryUnitOfWork<TDatabase>(registry, _publisher);
    }
}
