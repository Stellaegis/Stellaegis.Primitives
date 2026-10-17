namespace Stellaegis.Primitives.Data;

/// <summary>
/// Opens <see cref="IUnitOfWork"/> instances in a <see cref="Space"/>, for an <see cref="Actor"/>, on the database
/// designated by <typeparamref name="TDatabase"/>. The space may choose the database — one per compartment — and
/// both are set in the connection's session context, so that the database knows who works where: row-level
/// security, audit triggers.
/// </summary>
/// <remarks>
/// The unit of work is the same as one opened by <see cref="IUnitOfWorkFactory{TDatabase}"/>: without a
/// transaction, or with one, then as many successive transactions as needed. The space and the actor hold for its
/// whole life, transaction or not. An application with a single compartment opens in that compartment, never in
/// the shared space.
/// </remarks>
/// <typeparam name="TDatabase">The marker type of the database.</typeparam>
public interface ISpaceUnitOfWorkFactory<TDatabase>
{
    /// <summary>Opens a unit of work in <paramref name="space"/> for <paramref name="actor"/>. No transaction is started.</summary>
    Task<IUnitOfWork> OpenAsync(Space space, Actor actor, CancellationToken cancellationToken = default);

    /// <summary>Opens a unit of work in <paramref name="space"/> for <paramref name="actor"/>, and begins a transaction.</summary>
    Task<IUnitOfWork> OpenAndBeginAsync(Space space, Actor actor, CancellationToken cancellationToken = default);
}
