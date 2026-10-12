namespace Stellaegis.Primitives.Data;

/// <summary>
/// The catalog of repository factories of the database designated by <typeparamref name="TDatabase"/>,
/// populated once at startup and shared by every unit of work on that database. No reflection is used: the
/// single cast in <see cref="IUnitOfWork.Repo{TRepo}"/> is guaranteed correct by the registration site.
/// </summary>
/// <typeparam name="TDatabase">The marker type of the database these repositories belong to.</typeparam>
/// <remarks>
/// Reads are safe from multiple threads once population is complete; do not call <see cref="Add{TRepo}"/>
/// once the registry is being used concurrently.
/// </remarks>
public sealed class RepositoryRegistry<TDatabase>
{
    private readonly Dictionary<Type, RepositoryFactory<object>> _factories = [];

    /// <summary>Registers the factory that builds <typeparamref name="TRepo"/>.</summary>
    /// <typeparam name="TRepo">The repository contract to register.</typeparam>
    /// <param name="factory">Builds the repository from a connection and a transaction accessor.</param>
    /// <returns>This registry, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A factory is already registered for <typeparamref name="TRepo"/>.</exception>
    public RepositoryRegistry<TDatabase> Add<TRepo>(RepositoryFactory<TRepo> factory) where TRepo : class
    {
        Guard.NotNull(factory);

        // Covariant conversion: RepositoryFactory<TRepo> is a RepositoryFactory<object> since TRepo : class.
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

    internal RepositoryFactory<object> Resolve(Type repositoryType) =>
        _factories.TryGetValue(repositoryType, out var factory)
            ? factory
            : throw new RepositoryNotRegisteredException(repositoryType);
}
