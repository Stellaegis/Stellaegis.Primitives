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
/// PostgreSQL and SQL Server are written. On SQL Server, a script is split into batches on its <c>go</c> lines, run
/// with its record in one transaction; the application role is a database role, created when missing, which the
/// host makes its user a member of; it is granted its rights on the schema, <c>VIEW DEFINITION</c> included.
/// Extensions, row-level security and the scripts following the frame are not declared on SQL Server yet.
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
    /// <param name="applicationRole">
    /// The role the application connects under, granted usage and data rights, never DDL. On SQL Server, a database
    /// role, created when missing, which the host makes its user a member of.
    /// </param>
    /// <param name="actor">The actor recorded with each script applied; <see cref="DefaultActor"/> unless given.</param>
    /// <param name="cancellationToken">Cancels the installation between two scripts.</param>
    /// <remarks>
    /// Two installations of the same schema at once wait for each other, on a lock of the engine held for the
    /// installation of that schema.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="applicationRole"/> is <c>public</c>, which every role holds.</exception>
    /// <exception cref="InvalidOperationException">
    /// A script was applied with another content, the database is ahead of the package, or two declarations share a
    /// schema.
    /// </exception>
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
        if (string.Equals(role, "public", StringComparison.Ordinal))
            throw new ArgumentException("The application role cannot be public: every role holds what public is granted.", nameof(applicationRole));
        var declared = Declared(databases);
        var applied = new List<string>();
        var skipped = new List<string>();

        foreach (var database in declared)
        {
            var engine = Engine(database);
            await using var connection = await OpenAsync(openAsAdministrator, database, cancellationToken).ConfigureAwait(false);
            // Two installers at once — two instances deploying — wait for each other: the scripts are transactional,
            // and the frame scripts and the grants that follow them are not. Measured: four at once, one failed on the
            // grants with "tuple concurrently updated".
            await engine.LockAsync(database, connection, cancellationToken).ConfigureAwait(false);
            try
            {
                await engine.InstallAsync(database, frame, connection, role, actor ?? DefaultActor, applied, skipped, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await engine.UnlockAsync(database, connection, CancellationToken.None).ConfigureAwait(false);
            }
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
        var declared = Declared(databases);
        var faults = new List<string>();

        foreach (var database in declared)
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

    // A module's schema holds its _schema_migrations: two declarations on one schema would record their scripts in the
    // same table, and each read the other's as applied with another content. One declaration twice is said as such.
    private static List<ModuleDatabase> Declared(IEnumerable<ModuleDatabase> databases)
    {
        var declared = Guard.NotNull(databases).Select(database => Guard.NotNull(database)).ToList();
        foreach (var group in declared.GroupBy(database => (database.Engine, database.Schema)).Where(group => group.Count() > 1))
        {
            throw group.Distinct().Count() == 1
                ? new InvalidOperationException($"The database of {group.First().Name} is declared twice: its adapter is registered once.")
                : new InvalidOperationException(
                    $"The databases of {string.Join(" and ", group.Select(database => database.Name).Distinct())} share the schema "
                    + $"{group.Key.Schema}: each module has a schema of its own, which holds the record of its scripts.");
        }
        return declared;
    }

    private static async Task<DbConnection> OpenAsync(
        Func<ModuleDatabase, CancellationToken, Task<DbConnection>> open, ModuleDatabase database, CancellationToken cancellationToken)
    {
        var connection = await open(database, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No connection was opened for the database of {database.Name}.");
        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static IEngineLifecycle Engine(ModuleDatabase database) => database.Engine switch
    {
        DatabaseEngine.PostgreSql => PostgreSqlLifecycle.Instance,
        DatabaseEngine.SqlServer => SqlServerLifecycle.Instance,
        _ => throw new NotSupportedException($"The database of {database.Name} runs on {database.Engine}, which this package does not know."),
    };
}

/// <summary>What an engine must do for the life cycle: install under an administration role, verify under the application's.</summary>
internal interface IEngineLifecycle
{
    /// <summary>Takes the lock of the installation of <paramref name="database"/>'s schema, waiting for another installer holding it.</summary>
    Task LockAsync(ModuleDatabase database, DbConnection connection, CancellationToken ct);

    /// <summary>Releases what <see cref="LockAsync"/> took.</summary>
    Task UnlockAsync(ModuleDatabase database, DbConnection connection, CancellationToken ct);

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
