using System.Data.Common;
using System.Globalization;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// The life cycle on PostgreSQL. The installation owns <c>{schema}._schema_migrations</c>; the verification reads
/// it and the catalogue — <c>pg_extension</c>, <c>pg_class</c>, <c>pg_policies</c> —, which any role may read.
/// Every name reaching the SQL text was checked as an identifier by <see cref="ModuleDatabase"/>.
/// </summary>
internal sealed class PostgreSqlLifecycle : IEngineLifecycle
{
    public static PostgreSqlLifecycle Instance { get; } = new();

    private PostgreSqlLifecycle()
    {
    }

    public async Task InstallAsync(
        ModuleDatabase database, ApplicationFrame frame, DbConnection connection, string applicationRole, Actor actor,
        List<string> applied, List<string> skipped, CancellationToken ct)
    {
        var schema = database.Schema;
        await ExecuteAsync(connection, null, $"create schema if not exists {schema}", ct).ConfigureAwait(false);
        foreach (var extension in database.Extensions)
            await ExecuteAsync(connection, null, $"create extension if not exists {extension}", ct).ConfigureAwait(false);
        await ExecuteAsync(connection, null,
            $"create table if not exists {database.MigrationsTable} ("
            + "version smallint not null, script_name text not null, checksum text not null, "
            + "applied_at timestamptz not null default now(), applied_by text not null, "
            + $"constraint pk_{schema}_schema_migrations primary key (version))", ct).ConfigureAwait(false);

        var recorded = await ReadRecordedAsync(connection, database, ct).ConfigureAwait(false);
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
                        + $"corrected in place and the database reinstalled: drop schema {schema} cascade, then install again.");
                }
                skipped.Add($"{database.Name}: {script.FileName}");
                continue;
            }

            await using (var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false))
            {
                await ExecuteAsync(connection, transaction, script.Sql, ct).ConfigureAwait(false);
                await ExecuteAsync(connection, transaction,
                    $"insert into {database.MigrationsTable} (version, script_name, checksum, applied_by) values (@version, @name, @checksum, @actor)",
                    ct, ("version", (short)script.Version), ("name", script.Name), ("checksum", script.Checksum), ("actor", actor.Value)).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            applied.Add($"{database.Name}: {script.FileName}");
        }

        foreach (var frameScript in database.FrameScripts)
        {
            var declared = frame.Declares(frameScript.Feature);
            await ExecuteAsync(connection, null, declared ? frameScript.ApplySql : frameScript.RemoveSql, ct).ConfigureAwait(false);
            applied.Add($"{database.Name}: {(declared ? frameScript.ApplyName : frameScript.RemoveName)} ({frameScript.Feature}, {(declared ? "declared" : "not declared")})");
        }

        foreach (var grant in Grants(schema, database.MigrationsTable, applicationRole))
            await ExecuteAsync(connection, null, grant, ct).ConfigureAwait(false);
    }

    public async Task VerifyAsync(ModuleDatabase database, ApplicationFrame frame, DbConnection connection, List<string> faults, CancellationToken ct)
    {
        var installed = await ScalarAsync(connection, "select to_regclass(@table)::text", ct, ("table", database.MigrationsTable)).ConfigureAwait(false);
        if (installed is null or DBNull)
        {
            faults.Add($"{database.Name} is not installed: {database.MigrationsTable} does not exist");
            return;
        }

        var recorded = await ReadRecordedAsync(connection, database, ct).ConfigureAwait(false);
        foreach (var script in database.Scripts)
        {
            if (!recorded.TryGetValue(script.Version, out var record))
                faults.Add($"{database.Name}: {script.FileName} is not applied");
            else if (!string.Equals(record.Checksum, script.Checksum, StringComparison.Ordinal))
                faults.Add($"{database.Name}: {script.FileName} was applied with another content; reinstall the database");
        }
        foreach (var version in recorded.Keys.Where(version => version > database.Scripts.Count))
            faults.Add($"{database.Name}: the database holds version {version} ({recorded[version].Name}), which this package does not know");

        foreach (var extension in database.Extensions)
        {
            var present = await ScalarAsync(connection, "select count(*) from pg_extension where extname = @name", ct, ("name", extension)).ConfigureAwait(false);
            if (Convert.ToInt64(present, CultureInfo.InvariantCulture) == 0)
                faults.Add($"{database.Name}: the extension {extension} is not installed");
        }

        foreach (var table in database.SecuredTables)
        {
            var parts = table.Split('.');
            var security = await ReadRowSecurityAsync(connection, parts[0], parts[1], ct).ConfigureAwait(false);
            if (security is null)
                faults.Add($"{database.Name}: the table {table} does not exist");
            else if (!security.Value.Enabled || !security.Value.Forced)
                faults.Add($"{database.Name}: the table {table} is not under forced row-level security");
        }

        foreach (var policy in database.Policies)
        {
            if (!await PolicyExistsAsync(connection, policy, ct).ConfigureAwait(false))
                faults.Add($"{database.Name}: the policy {policy.Policy} is missing on {policy.Table}");
        }

        foreach (var frameScript in database.FrameScripts)
        {
            var declared = frame.Declares(frameScript.Feature);
            foreach (var policy in frameScript.Policies)
            {
                var exists = await PolicyExistsAsync(connection, policy, ct).ConfigureAwait(false);
                if (declared && !exists)
                    faults.Add($"{database.Name}: the policy {policy.Policy} on {policy.Table} is missing, and the frame declares {frameScript.Feature}");
                else if (!declared && exists)
                    faults.Add($"{database.Name}: the policy {policy.Policy} on {policy.Table} is present, and the frame does not declare {frameScript.Feature}");
            }
        }
    }

    // The application reads and writes data, reads the scripts applied, and creates nothing: what a script creates
    // later is granted by the default privileges of the installing role, in the schema.
    private static IEnumerable<string> Grants(string schema, string migrationsTable, string role) =>
    [
        $"grant usage on schema {schema} to {role}",
        $"grant select, insert, update, delete on all tables in schema {schema} to {role}",
        $"grant usage, select on all sequences in schema {schema} to {role}",
        $"grant execute on all functions in schema {schema} to {role}",
        $"alter default privileges in schema {schema} grant select, insert, update, delete on tables to {role}",
        $"alter default privileges in schema {schema} grant usage, select on sequences to {role}",
        $"alter default privileges in schema {schema} grant execute on functions to {role}",
        $"revoke insert, update, delete on {migrationsTable} from {role}",
    ];

    private static async Task<Dictionary<int, (string Name, string Checksum)>> ReadRecordedAsync(DbConnection connection, ModuleDatabase database, CancellationToken ct)
    {
        var recorded = new Dictionary<int, (string Name, string Checksum)>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"select version, script_name, checksum from {database.MigrationsTable}";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            recorded[Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)] = (reader.GetString(1), reader.GetString(2));
        return recorded;
    }

    private static async Task<(bool Enabled, bool Forced)?> ReadRowSecurityAsync(DbConnection connection, string schema, string table, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select c.relrowsecurity, c.relforcerowsecurity from pg_class c join pg_namespace n on n.oid = c.relnamespace "
            + "where n.nspname = @schema and c.relname = @table";
        AddParameter(command, "schema", schema);
        AddParameter(command, "table", table);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? (reader.GetBoolean(0), reader.GetBoolean(1)) : null;
    }

    private static async Task<bool> PolicyExistsAsync(DbConnection connection, RowPolicy policy, CancellationToken ct)
    {
        var parts = policy.Table.Split('.');
        var count = await ScalarAsync(connection,
            "select count(*) from pg_policies where schemaname = @schema and tablename = @table and policyname = @policy", ct,
            ("schema", parts[0]), ("table", parts[1]), ("policy", policy.Policy)).ConfigureAwait(false);
        return Convert.ToInt64(count, CultureInfo.InvariantCulture) > 0;
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
