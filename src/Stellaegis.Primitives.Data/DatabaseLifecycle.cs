using System.Data.Common;

namespace Stellaegis.Primitives.Data;

/// <summary>What an installation did: the scripts it applied, those already applied and skipped, module by module.</summary>
public sealed record InstallationReport(IReadOnlyList<string> Applied, IReadOnlyList<string> Skipped);

/// <summary>
/// The life cycle of the databases a module declares (<see cref="ModuleDatabase"/>): their installation, by an
/// executant that holds an administration role — an installer, a CLI, the provisioner of the application —, and
/// their verification, by the application at every startup, under its own role and in read only.
/// <code>
/// // the installer, under the role that owns the schemas
/// await DatabaseLifecycle.InstallAsync(databases, AppFrame.Value, (_, ct) => adminSource.OpenConnectionAsync(ct).AsTask(), "myapp", ct: ct);
///
/// // the application, at every startup, under its own role
/// await DatabaseLifecycle.VerifyAsync(databases, AppFrame.Value, (_, ct) => appSource.OpenConnectionAsync(ct).AsTask(), ct);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The installation is replayable: a script already applied with the same checksum is skipped, the scripts that
/// follow the frame are applied or removed to match it, and the grants are posed again. The verification fails,
/// naming every module and what is missing, when a script is not applied or was applied with another content,
/// when an extension or a policy the frame requires is absent, when a policy the frame does not declare is
/// present, or when a table is not under forced row-level security. It is the proof that the installation ran,
/// and that nothing changed since.
/// </para>
/// <para>
/// PostgreSQL is written. SQL Server is declared and refused by both, until an adapter needs it.
/// </para>
/// </remarks>
public static class DatabaseLifecycle
{
    /// <summary>The actor the installation records when none is given, <c>job:install</c>.</summary>
    public static Actor DefaultActor { get; } = Actor.Job("install");

    /// <summary>
    /// Installs the databases, each on a connection <paramref name="openAsAdministrator"/> opens for it under a
    /// role that owns its schema, and grants <paramref name="applicationRole"/> what the application needs.
    /// </summary>
    /// <param name="databases">The databases declared by the modules of the application.</param>
    /// <param name="frame">The frame of the application, which the scripts following it match.</param>
    /// <param name="openAsAdministrator">Opens the connection of a module's database, under the administration role. Disposed after use.</param>
    /// <param name="applicationRole">The role the application connects under, granted usage and data rights, never DDL.</param>
    /// <param name="actor">The actor recorded with each script applied; <see cref="DefaultActor"/> unless given.</param>
    /// <param name="cancellationToken">Cancels the installation between two scripts.</param>
    /// <exception cref="InvalidOperationException">A script was applied with another content, or the database is ahead of the package.</exception>
    /// <exception cref="NotSupportedException">The engine of a database is not written in this package.</exception>
    public static async Task<InstallationReport> InstallAsync(
        IEnumerable<ModuleDatabase> databases,
        ApplicationFrame frame,
        Func<ModuleDatabase, CancellationToken, Task<DbConnection>> openAsAdministrator,
        string applicationRole,
        Actor? actor = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(frame);
        Guard.NotNull(openAsAdministrator);
        var role = ModuleDatabase.CheckIdentifier(applicationRole, "role");
        var applied = new List<string>();
        var skipped = new List<string>();

        foreach (var database in Guard.NotNull(databases).Select(database => Guard.NotNull(database)))
        {
            var engine = Engine(database);
            await using var connection = await OpenAsync(openAsAdministrator, database, cancellationToken).ConfigureAwait(false);
            await engine.InstallAsync(database, frame, connection, role, actor ?? DefaultActor, applied, skipped, cancellationToken).ConfigureAwait(false);
        }
        return new InstallationReport(applied, skipped);
    }

    /// <summary>
    /// Verifies the databases, each on a connection <paramref name="open"/> opens for it under the application's
    /// role, in read only, and fails naming every module and what is missing.
    /// </summary>
    /// <exception cref="InvalidOperationException">A database does not hold with its declaration and the frame.</exception>
    /// <exception cref="NotSupportedException">The engine of a database is not written in this package.</exception>
    public static async Task VerifyAsync(
        IEnumerable<ModuleDatabase> databases,
        ApplicationFrame frame,
        Func<ModuleDatabase, CancellationToken, Task<DbConnection>> open,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(frame);
        Guard.NotNull(open);
        var faults = new List<string>();

        foreach (var database in Guard.NotNull(databases).Select(database => Guard.NotNull(database)))
        {
            var engine = Engine(database);
            await using var connection = await OpenAsync(open, database, cancellationToken).ConfigureAwait(false);
            await engine.VerifyAsync(database, frame, connection, faults, cancellationToken).ConfigureAwait(false);
        }

        if (faults.Count > 0)
        {
            throw new InvalidOperationException(
                $"The databases do not hold with the application frame ({frame}): {string.Join("; ", faults)}. Run the "
                + "installation of the application under its administration role — DatabaseLifecycle.InstallAsync, or the "
                + "installer that calls it —, then start again.");
        }
    }

    private static async Task<DbConnection> OpenAsync(
        Func<ModuleDatabase, CancellationToken, Task<DbConnection>> open, ModuleDatabase database, CancellationToken cancellationToken)
    {
        var connection = await open(database, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No connection was opened for the database of {database.Name}.");
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static IEngineLifecycle Engine(ModuleDatabase database) => database.Engine switch
    {
        DatabaseEngine.PostgreSql => PostgreSqlLifecycle.Instance,
        _ => throw new NotSupportedException(
            $"The database of {database.Name} runs on {database.Engine}, which this package neither installs nor verifies yet: "
            + "only PostgreSQL is written."),
    };
}

/// <summary>What an engine must do for the life cycle: install under an administration role, verify under the application's.</summary>
internal interface IEngineLifecycle
{
    Task InstallAsync(
        ModuleDatabase database, ApplicationFrame frame, DbConnection connection, string applicationRole, Actor actor,
        List<string> applied, List<string> skipped, CancellationToken cancellationToken);

    Task VerifyAsync(ModuleDatabase database, ApplicationFrame frame, DbConnection connection, List<string> faults, CancellationToken cancellationToken);
}

/// <summary>Which features of the frame a database follows.</summary>
internal static class FrameFeatures
{
    public static bool Declares(this ApplicationFrame frame, FrameFeature feature) => feature switch
    {
        FrameFeature.OwnerIsolation => frame.OwnerIsolation,
        _ => false,
    };
}
