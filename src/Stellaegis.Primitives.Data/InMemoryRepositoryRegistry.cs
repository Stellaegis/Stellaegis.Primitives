namespace Stellaegis.Primitives.Data;

/// <summary>
/// The catalog of in-memory repository factories of the database designated by <typeparamref name="TDatabase"/>.
/// An in-memory repository has neither connection nor transaction, so its factory is a plain
/// <see cref="Func{TRepo}"/>.
/// </summary>
/// <typeparam name="TDatabase">The marker type of the database these repositories stand in for.</typeparam>
/// <remarks>
/// Whether data survives from one unit of work to the next is the factory's choice: return a shared instance
/// and every unit of work sees one store, as with a database; return a fresh one for per-operation state.
/// Implement <see cref="IRestorableRepository"/> on the repository to have rollbacks undo its writes.
/// </remarks>
public sealed class InMemoryRepositoryRegistry<TDatabase>
{
    private readonly Dictionary<Type, Func<object>> _factories = [];

    /// <summary>Registers the factory that builds <typeparamref name="TRepo"/>.</summary>
    /// <typeparam name="TRepo">The repository contract to register.</typeparam>
    /// <param name="factory">Builds the repository. Capture a shared instance to persist state across units of work.</param>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A factory is already registered for <typeparamref name="TRepo"/>.</exception>
    public InMemoryRepositoryRegistry<TDatabase> Add<TRepo>(Func<TRepo> factory) where TRepo : class
    {
        Guard.NotNull(factory);

        // Covariant conversion: Func<TRepo> is a Func<object> since TRepo : class.
        if (!_factories.TryAdd(typeof(TRepo), factory))
            throw new InvalidOperationException(
                $"A factory is already registered for repository type '{typeof(TRepo).FullName}'.");

        return this;
    }

    /// <summary>Whether a factory is registered for <typeparamref name="TRepo"/>.</summary>
    /// <typeparam name="TRepo">The repository contract to look up.</typeparam>
    public bool Contains<TRepo>() where TRepo : class => _factories.ContainsKey(typeof(TRepo));

    /// <summary>The repository types that have a registered factory.</summary>
    public IReadOnlyCollection<Type> RegisteredTypes => _factories.Keys;

    internal Func<object> Resolve(Type repositoryType) =>
        _factories.TryGetValue(repositoryType, out var factory)
            ? factory
            : throw new RepositoryNotRegisteredException(repositoryType);
}
