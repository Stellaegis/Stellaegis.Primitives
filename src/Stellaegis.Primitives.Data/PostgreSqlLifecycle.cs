using System.Data.Common;
using System.Globalization;

namespace Stellaegis.Primitives.Data;

/// <summary>
/// The life cycle on PostgreSQL. The installation owns <c>{schema}._schema_migrations</c>; the verification reads
/// it and the catalogue — <c>pg_extension</c>, <c>pg_class</c>, <c>pg_policies</c>, <c>pg_roles</c>, <c>pg_proc</c> —,
/// which any role may read. Every name reaching the SQL text was checked as an identifier by <see cref="ModuleDatabase"/>.
/// </summary>
/// <remarks>
/// The verification proves the isolation, not only that the installation ran: row-level security binds the role
/// the application connects with only if that role is neither a superuser nor <c>BYPASSRLS</c>, owns nothing in the
/// schema and creates nothing there; and only the policies declared may apply, none written more widely, none read
/// around by a view or a function running under its owner.
/// </remarks>
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
                        $"{database.Name}: {script.FileName} was applied with another content. A script once applied is never "
                        + "corrected, and the database is never dropped to take it again: restore the script as it was applied, "
                        + $"and write the change as a script of its own, numbered after {database.Scripts[^1].FileName}.");
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

        await VerifyApplicationRoleAsync(connection, database, faults, ct).ConfigureAwait(false);

        var recorded = await ReadRecordedAsync(connection, database, ct).ConfigureAwait(false);
        foreach (var script in database.Scripts)
        {
            if (!recorded.TryGetValue(script.Version, out var record))
                faults.Add($"{database.Name}: {script.FileName} is not applied");
            else if (!string.Equals(record.Checksum, script.Checksum, StringComparison.Ordinal))
                faults.Add($"{database.Name}: {script.FileName} was applied with another content; restore the script as it was applied, and write the change as a new numbered script");
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

        if (database.SecuredTables.Count > 0)
        {
            await VerifyPoliciesAsync(connection, database, frame, faults, ct).ConfigureAwait(false);
            await VerifyReadersAroundAsync(connection, database, faults, ct).ConfigureAwait(false);
        }
    }

    // Row-level security binds a role that is neither a superuser nor BYPASSRLS, and that owns nothing it could
    // unforce or drop: the role the application connects with is proven to be one. Measured: connected as postgres, as
    // a BYPASSRLS role or as the owner of the schema, the application of acme read the rows of every compartment, and
    // the verification passed.
    private static async Task VerifyApplicationRoleAsync(DbConnection connection, ModuleDatabase database, List<string> faults, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            select r.rolsuper, r.rolbypassrls,
                   coalesce(pg_has_role(current_user, n.nspowner, 'MEMBER'), false),
                   coalesce(has_schema_privilege(current_user, n.oid, 'CREATE'), false),
                   (select string_agg(c.relname, ', ' order by c.relname) from pg_class c
                    where c.relnamespace = n.oid and c.relkind in ('r', 'p', 'v', 'm', 'S', 'f')
                      and pg_has_role(current_user, c.relowner, 'MEMBER')),
                   current_user::text
            from pg_roles r left join pg_namespace n on n.nspname = @schema
            where r.rolname = current_user
            """;
        AddParameter(command, "schema", database.Schema);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return;
        var role = reader.GetString(5);
        const string Advice = "the application connects with a role of its own, which the installation grants";
        if (reader.GetBoolean(0))
        {
            faults.Add($"{database.Name}: the application connects as {role}, a superuser, which row-level security does not bind; {Advice}");
            return;
        }
        if (reader.GetBoolean(1))
            faults.Add($"{database.Name}: the application connects as {role}, which bypasses row-level security (BYPASSRLS); {Advice}");
        if (reader.GetBoolean(2))
            faults.Add($"{database.Name}: the application connects as {role}, which owns the schema {database.Schema}; {Advice}");
        if (!reader.IsDBNull(4))
            faults.Add($"{database.Name}: the application connects as {role}, which owns {reader.GetString(4)} in {database.Schema}; {Advice}");
        if (reader.GetBoolean(3))
            faults.Add($"{database.Name}: the application connects as {role}, which may create objects in {database.Schema}; {Advice}");
    }

    // Policies add up: a permissive one more opens what the declared ones keep closed, whatever its name. Every policy
    // of a secured table is declared, by the module or by the frame; a policy of the frame narrows, and is restrictive.
    private static async Task VerifyPoliciesAsync(DbConnection connection, ModuleDatabase database, ApplicationFrame frame, List<string> faults, CancellationToken ct)
    {
        var declared = database.Policies
            .Concat(database.FrameScripts.Where(script => frame.Declares(script.Feature)).SelectMany(script => script.Policies))
            .ToHashSet();
        var narrowing = database.FrameScripts.SelectMany(script => script.Policies).ToHashSet();
        foreach (var table in database.SecuredTables)
        {
            var parts = table.Split('.');
            await using var command = connection.CreateCommand();
            command.CommandText = "select policyname, permissive from pg_policies where schemaname = @schema and tablename = @table order by policyname";
            AddParameter(command, "schema", parts[0]);
            AddParameter(command, "table", parts[1]);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var policy = new RowPolicy(table, reader.GetString(0));
                var restrictive = string.Equals(reader.GetString(1), "RESTRICTIVE", StringComparison.OrdinalIgnoreCase);
                if (!declared.Contains(policy) && !narrowing.Contains(policy))
                    faults.Add($"{database.Name}: the policy {policy.Policy} on {table} is not declared; every policy of a table under row-level security is");
                else if (narrowing.Contains(policy) && !restrictive)
                    faults.Add($"{database.Name}: the policy {policy.Policy} on {table} follows the frame and is permissive; it narrows, and is written as restrictive");
            }
        }
    }

    // A view reads its tables as its owner unless it is security_invoker, and a materialized view holds the rows of
    // whoever refreshed it: in a schema under row-level security, either reads around the policies. So does a
    // SECURITY DEFINER function whose owner is not bound by them. Measured: installed as postgres, a view of acme
    // returned the rows of globex, and the verification passed.
    private static async Task VerifyReadersAroundAsync(DbConnection connection, ModuleDatabase database, List<string> faults, CancellationToken ct)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select c.relname, c.relkind::text, coalesce(c.reloptions, '{}'::text[])
                from pg_class c join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = @schema and c.relkind in ('v', 'm')
                order by c.relname
                """;
            AddParameter(command, "schema", database.Schema);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var name = $"{database.Schema}.{reader.GetString(0)}";
                if (reader.GetString(1) == "m")
                    faults.Add($"{database.Name}: the materialized view {name} holds rows outside row-level security; a schema under it has none");
                else if (!IsSecurityInvoker((string[])reader.GetValue(2)))
                    faults.Add($"{database.Name}: the view {name} reads as its owner, around row-level security; create it with (security_invoker = true)");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select p.proname from pg_proc p
                join pg_namespace n on n.oid = p.pronamespace
                join pg_roles r on r.oid = p.proowner
                where n.nspname = @schema and p.prosecdef and (r.rolsuper or r.rolbypassrls)
                order by p.proname
                """;
            AddParameter(command, "schema", database.Schema);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                faults.Add($"{database.Name}: the function {database.Schema}.{reader.GetString(0)} runs as an owner row-level security does not bind (SECURITY DEFINER)");
        }
    }

    // PostgreSQL keeps the option as written: true, on, yes, 1, in any case.
    private static bool IsSecurityInvoker(string[] options)
        => options.Any(option => option.Split('=', 2) is [var key, var value]
            && string.Equals(key.Trim(), "security_invoker", StringComparison.OrdinalIgnoreCase)
            && value.Trim().ToLowerInvariant() is "true" or "on" or "yes" or "1" or "t" or "y");

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
