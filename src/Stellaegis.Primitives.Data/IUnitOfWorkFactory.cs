namespace Stellaegis.Primitives.Data;

/// <summary>
/// Opens <see cref="IUnitOfWork"/> instances on the database designated by <typeparamref name="TDatabase"/>,
/// one per operation. The relational and the in-memory implementations both satisfy it, so switching
/// between them is a composition-root decision.
/// </summary>
/// <typeparam name="TDatabase">
/// A marker type declared by the module for its database — an empty <c>sealed class</c>. It lets a host
/// that composes several modules register one factory per database and resolve each without keyed
/// services.
/// </typeparam>
public interface IUnitOfWorkFactory<TDatabase>
{
    /// <summary>Opens a unit of work, ready to resolve repositories. No transaction is started.</summary>
    Task<IUnitOfWork> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a unit of work and begins a transaction before returning it.</summary>
    Task<IUnitOfWork> OpenAndBeginAsync(CancellationToken cancellationToken = default);
}
