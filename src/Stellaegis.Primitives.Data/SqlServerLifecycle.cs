using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// The life cycle on SQL Server. The installation owns <c>{schema}._schema_migrations</c> and the application role;
/// the verification reads that table, which the role reaches through its rights on the schema. A script is split
/// into batches on its <c>go</c> lines, and its batches run with its record in one transaction: the DDL of SQL
/// Server is transactional, so a script that fails leaves nothing behind. Every name reaching the SQL text was
/// checked as an identifier by <see cref="ModuleDatabase"/>.
/// </summary>
internal sealed partial class SqlServerLifecycle : IEngineLifecycle
{
    public static SqlServerLifecycle Instance { get; } = new();

    private SqlServerLifecycle()
    {
    }

    public async Task InstallAsync(
        ModuleDatabase database, ApplicationFrame frame, DbConnection connection, string applicationRole, Actor actor,
        List<string> applied, List<string> skipped, CancellationToken ct)
    {
        var schema = database.Schema;
        var migrations = Migrations(database);
        await ExecuteAsync(connection, null, $"if schema_id(N'{schema}') is null exec(N'create schema [{schema}] authorization dbo')", ct).ConfigureAwait(false);
        await ExecuteAsync(connection, null, $"if database_principal_id(N'{applicationRole}') is null exec(N'create role [{applicationRole}]')", ct).ConfigureAwait(false);
        await ExecuteAsync(connection, null,
            $"if object_id(N'{database.MigrationsTable}', N'U') is null create table {migrations} ("
            + "version smallint not null, script_name nvarchar(128) not null, checksum char(64) not null, "
            + $"applied_at datetimeoffset(3) not null constraint df_{schema}_schema_migrations_applied_at default sysdatetimeoffset(), "
            + "applied_by nvarchar(128) not null, "
            + $"constraint pk_{schema}_schema_migrations primary key (version))", ct).ConfigureAwait(false);

        var recorded = await ReadRecordedAsync(connection, migrations, ct).ConfigureAwait(false);
        foreach (var version in recorded.Keys.Where(version => version > database.Scripts.Count))
        {
            throw new InvalidOperationException(
                $"{database.Name}: the database holds version {version} ({recorded[version].Name}), which this package does not know: "
                + "the package is behind the database.");
        }

        foreach (var script in database.Scripts)
        {
            if (recorded.TryGetValue(script.Version, out var record))
            {
                if (!string.Equals(record.Checksum, script.Checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{database.Name}: {script.FileName} was applied with another content. While no real data exists, a script is "
                        + $"corrected in place and the database reinstalled: drop the objects of schema {schema} and the schema, or "
                        + "recreate the database, then install again.");
                }
                skipped.Add($"{database.Name}: {script.FileName}");
                continue;
            }

            await using (var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false))
            {
                foreach (var batch in Batches(script.Sql))
                    await ExecuteAsync(connection, transaction, batch, ct).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction,
                    $"insert into {migrations} (version, script_name, checksum, applied_by) values (@version, @name, @checksum, @actor)",
                    ct, ("version", (short)script.Version), ("name", script.Name), ("checksum", script.Checksum), ("actor", actor.Value)).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            applied.Add($"{database.Name}: {script.FileName}");
        }

        // The rights on the schema reach the objects a script creates later, without default privileges. VIEW
        // DEFINITION lets the verification read the catalogue under the application role.
        await ExecuteAsync(connection, null,
            $"grant select, insert, update, delete, execute, view definition on schema::[{schema}] to [{applicationRole}]", ct).ConfigureAwait(false);
        await ExecuteAsync(connection, null, $"deny insert, update, delete on {migrations} to [{applicationRole}]", ct).ConfigureAwait(false);
    }

    public async Task VerifyAsync(ModuleDatabase database, ApplicationFrame frame, DbConnection connection, List<string> faults, CancellationToken ct)
    {
        var installed = await ScalarAsync(connection, "select object_id(@table, N'U')", ct, ("table", database.MigrationsTable)).ConfigureAwait(false);
        if (installed is null or DBNull)
        {
            faults.Add($"{database.Name} is not installed: {database.MigrationsTable} does not exist");
            return;
        }

        var recorded = await ReadRecordedAsync(connection, Migrations(database), ct).ConfigureAwait(false);
        foreach (var script in database.Scripts)
        {
            if (!recorded.TryGetValue(script.Version, out var record))
                faults.Add($"{database.Name}: {script.FileName} is not applied");
            else if (!string.Equals(record.Checksum, script.Checksum, StringComparison.Ordinal))
                faults.Add($"{database.Name}: {script.FileName} was applied with another content; reinstall the database");
        }
        foreach (var version in recorded.Keys.Where(version => version > database.Scripts.Count))
            faults.Add($"{database.Name}: the database holds version {version} ({recorded[version].Name}), which this package does not know");
    }

    /// <summary>
    /// The batches of <paramref name="sql"/>, split on the lines that hold only <c>go</c>, a separator of the client
    /// tools that the engine does not know; blank batches are dropped.
    /// </summary>
    internal static IReadOnlyList<string> Batches(string sql)
        => GoLine().Split(sql.Replace("\r\n", "\n", StringComparison.Ordinal))
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .Select(batch => batch.Trim())
            .ToList();

    [GeneratedRegex(@"^[ \t]*go[ \t]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoLine();

    private static string Migrations(ModuleDatabase database) => $"[{database.Schema}].[_schema_migrations]";

    private static async Task<Dictionary<int, (string Name, string Checksum)>> ReadRecordedAsync(DbConnection connection, string migrations, CancellationToken ct)
    {
        var recorded = new Dictionary<int, (string Name, string Checksum)>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select version, script_name, checksum from {migrations}";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            recorded[Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] = (reader.GetString(1), reader.GetString(2));
        return recorded;
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            AddParameter(command, name, value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            AddParameter(command, name, value);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
