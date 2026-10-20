namespace Stellaegis.Primitives;

/// <summary>
/// The compartments of an application, for the work that acts on each of them outside a request — purging the
/// compartments a tenant resigned from, verifying each one at startup. With a single compartment, the frame
/// provides it: the one constant. With many, the application registers its own — the registry of its tenants.
/// Without compartments, there is none.
/// <code>
/// await foreach (var compartment in directory.ListAsync(ct))
///     await documents.PurgeCompartmentAsync(compartment, Actor.Parse("job:purge"), ct);
/// </code>
/// </summary>
/// <remarks>
/// The directory enumerates; it decides nothing. Which compartments a piece of work should act on — those
/// terminated for thirty days, for instance — is the application's question, answered on what the directory lists
/// or by a port of the application's own.
/// </remarks>
public interface ICompartmentDirectory
{
    /// <summary>The compartments of the application, each once.</summary>
    IAsyncEnumerable<Compartment> ListAsync(CancellationToken cancellationToken = default);
}
