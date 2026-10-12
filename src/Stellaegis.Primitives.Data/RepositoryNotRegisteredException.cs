namespace Stellaegis.Primitives.Data;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.Repo{TRepo}"/> when the requested repository type has no registered
/// factory. Signals a composition gap — the intent is to fail fast on first use.
/// </summary>
/// <param name="repositoryType">The repository type that could not be resolved.</param>
public sealed class RepositoryNotRegisteredException(Type repositoryType)
    : InvalidOperationException(
        $"No factory is registered for repository type '{repositoryType.FullName}'. " +
        "Register it on the repository registry at startup before resolving it.")
{
    /// <summary>The repository type that was requested but not registered.</summary>
    public Type RepositoryType { get; } = repositoryType;
}
