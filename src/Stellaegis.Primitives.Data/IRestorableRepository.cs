namespace Stellaegis.Primitives.Data;

/// <summary>
/// Implemented by an in-memory repository that wants its state restored when a transaction rolls back.
/// The in-memory unit of work takes a snapshot when a transaction begins — or when the repository is first
/// resolved during one — and restores it on rollback.
/// </summary>
/// <remarks>
/// A snapshot copies the collection, not the rows: a row mutated in place is not restored. Keep rows
/// immutable — <c>record</c> types replaced on update — and a copy of the collection is enough. Two units of
/// work running at the same time on the same store are not isolated: a rollback in one erases the writes of
/// the other.
/// </remarks>
public interface IRestorableRepository
{
    /// <summary>Returns a copy of the repository's state — <c>_rows.ToList()</c>, for instance.</summary>
    object Snapshot();

    /// <summary>Puts back a state returned by <see cref="Snapshot"/>.</summary>
    void Restore(object snapshot);
}
