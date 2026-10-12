using System.Data.Common;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// Builds a repository of type <typeparamref name="TRepo"/> from a unit of work's <paramref name="connection"/>
/// and a late-bound accessor to its <paramref name="currentTransaction"/>, which returns <see langword="null"/>
/// when no transaction is active.
/// </summary>
/// <typeparam name="TRepo">The repository contract the factory produces.</typeparam>
/// <param name="connection">The unit of work's open connection.</param>
/// <param name="currentTransaction">
/// Accessor to the unit of work's current transaction, read each time the repository needs it. The repository
/// stays blind to the transaction lifecycle: it forwards whatever this accessor returns to each command.
/// </param>
/// <returns>A new repository instance.</returns>
public delegate TRepo RepositoryFactory<out TRepo>(
    DbConnection connection,
    Func<DbTransaction?> currentTransaction)
    where TRepo : class;
