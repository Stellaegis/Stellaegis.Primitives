using System.Data.Common;
using Npgsql;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>
/// The life cycle on a real PostgreSQL, named by <c>STELLAEGIS_TEST_POSTGRESQL</c> and skipped without it: the
/// schema <c>lcw</c> is dropped and reinstalled by every test, under the connection of the variable, which owns it;
/// the application role <c>lcw_app</c> is created once, a login without superuser rights.
/// </summary>
public sealed class DatabaseLifecycleTests : IAsyncLifetime
{
    private const string Schema = "lcw";
    private const string AppRole = "lcw_app";
    private const string ResourcePrefix = "Stellaegis.Primitives.Tests.Data.Sql.PostgreSql";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class WidgetsModule : IStellaegisModule
    {
        public static string Name => "widgets";
    }

    private static readonly ApplicationFrame Acme = ApplicationFrame.SingleCompartment("acme");
    private static readonly ApplicationFrame AcmeIsolated = Acme.WithOwnerIsolation();

    private static readonly System.Reflection.Assembly Tests = typeof(DatabaseLifecycleTests).Assembly;

    private static ModuleDatabase Widgets(IEnumerable<SqlScript>? scripts = null) => ModuleDatabase.PostgreSql<WidgetsModule>(Schema)
        .WithScripts(scripts ?? SqlScripts.FromResources(Tests, ResourcePrefix))
        .RequiringExtension("pgcrypto")
        .WithRowSecurity("lcw.widgets", "pl_widgets_space")
        .WhenFrameDeclares(FrameFeature.OwnerIsolation,
            "owner_isolation", SqlScripts.ReadResource(Tests, $"{ResourcePrefix}.owner_isolation.sql"),
            "owner_isolation_remove", SqlScripts.ReadResource(Tests, $"{ResourcePrefix}.owner_isolation_remove.sql"),
            new RowPolicy("lcw.widgets", "pl_widgets_owner"));

    private string _admin = null!;
    private string _app = null!;

    public async ValueTask InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable("STELLAEGIS_TEST_POSTGRESQL");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), "STELLAEGIS_TEST_POSTGRESQL is not set.");
        _admin = value!;
        _app = new NpgsqlConnectionStringBuilder(_admin) { Username = AppRole, Password = AppRole }.ConnectionString;

        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            drop schema if exists {Schema} cascade;
            do $$ begin
              if not exists (select 1 from pg_roles where rolname = '{AppRole}') then
                create role {AppRole} login password '{AppRole}';
              end if;
            end $$;
            alter role {AppRole} nobypassrls;
            """;
        await command.ExecuteNonQueryAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task<DbConnection> OpenAdminAsync(ModuleDatabase _, CancellationToken ct) => OpenAsync(_admin, ct);

    private Task<DbConnection> OpenAppAsync(ModuleDatabase _, CancellationToken ct) => OpenAsync(_app, ct);

    private static async Task<DbConnection> OpenAsync(string connectionString, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private Task<InstallationReport> InstallAsync(ApplicationFrame frame, ModuleDatabase? database = null)
        => DatabaseLifecycle.InstallAsync([database ?? Widgets()], frame, OpenAdminAsync, AppRole, cancellationToken: Ct);

    private Task VerifyAsync(ApplicationFrame frame, ModuleDatabase? database = null, bool asApplication = true)
        => DatabaseLifecycle.VerifyAsync([database ?? Widgets()], frame, asApplication ? OpenAppAsync : OpenAdminAsync, Ct);

    [Fact]
    public async Task The_installation_applies_the_scripts_once_and_the_application_verifies_them()
    {
        var first = await InstallAsync(Acme);
        Assert.Equal(["widgets: 0001_widgets.sql", "widgets: 0002_widget_tags.sql", "widgets: owner_isolation_remove (OwnerIsolation, not declared)"], first.Applied);
        Assert.Empty(first.Skipped);

        var again = await InstallAsync(Acme);
        Assert.Equal(["widgets: owner_isolation_remove (OwnerIsolation, not declared)"], again.Applied);
        Assert.Equal(["widgets: 0001_widgets.sql", "widgets: 0002_widget_tags.sql"], again.Skipped);

        await VerifyAsync(Acme);

        // The application role reads what was applied and writes data, and cannot touch the record of the scripts.
        await using var app = await OpenAsync(_app, Ct);
        await using (var command = app.CreateCommand())
        {
            command.CommandText = "select count(*), max(applied_by) from lcw._schema_migrations";
            await using var reader = await command.ExecuteReaderAsync(Ct);
            Assert.True(await reader.ReadAsync(Ct));
            Assert.Equal(2, reader.GetInt64(0));
            Assert.Equal("job:install", reader.GetString(1));
        }
        await using (var command = app.CreateCommand())
        {
            command.CommandText = "insert into lcw._schema_migrations (version, script_name, checksum, applied_by) values (9, 'x', 'x', 'x')";
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        }
    }

    [Fact]
    public async Task A_script_applied_with_another_content_fails_the_verification_and_the_installation()
    {
        await InstallAsync(Acme);
        var scripts = SqlScripts.FromResources(Tests, ResourcePrefix);
        var changed = Widgets([new SqlScript(1, scripts[0].Name, scripts[0].Sql + "\n-- corrected"), scripts[1]]);

        var verification = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme, changed));
        Assert.Contains("widgets: 0001_widgets.sql was applied with another content", verification.Message);
        Assert.Contains("InstallAsync", verification.Message);

        var installation = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(Acme, changed));
        Assert.Contains("numbered after 0002_widget_tags.sql", installation.Message);
        Assert.DoesNotContain("drop schema", installation.Message);
    }

    [Fact]
    public async Task The_isolation_by_owner_follows_the_frame()
    {
        await InstallAsync(AcmeIsolated);
        await VerifyAsync(AcmeIsolated);

        var undeclared = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme));
        Assert.Contains("pl_widgets_owner on lcw.widgets is present, and the frame does not declare OwnerIsolation", undeclared.Message);

        await InstallAsync(Acme);
        await VerifyAsync(Acme);

        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(AcmeIsolated));
        Assert.Contains("pl_widgets_owner on lcw.widgets is missing, and the frame declares OwnerIsolation", missing.Message);
    }

    [Fact]
    public async Task A_database_not_installed_names_the_installation()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme, asApplication: false));

        Assert.Contains("widgets is not installed: lcw._schema_migrations does not exist", exception.Message);
        Assert.Contains("DatabaseLifecycle.InstallAsync", exception.Message);
    }

    [Fact]
    public async Task A_database_ahead_of_the_package_fails_the_verification()
    {
        await InstallAsync(Acme);
        var behind = Widgets([SqlScripts.FromResources(Tests, ResourcePrefix)[0]]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme, behind));
        Assert.Contains("holds version 2 (widget_tags), which this package does not know", exception.Message);
    }

    [Fact]
    public async Task What_the_frame_requires_of_the_catalogue_is_verified()
    {
        await InstallAsync(Acme);
        await using (var admin = await OpenAsync(_admin, Ct))
        await using (var command = admin.CreateCommand())
        {
            command.CommandText = "alter table lcw.widgets no force row level security; drop policy pl_widgets_space on lcw.widgets";
            await command.ExecuteNonQueryAsync(Ct);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme));
        Assert.Contains("lcw.widgets is not under forced row-level security", exception.Message);
        Assert.Contains("the policy pl_widgets_space is missing on lcw.widgets", exception.Message);
    }

    // Four installers at once — four instances deploying —: one failed on the grants before the lock.
    [Fact]
    public async Task Installations_at_once_wait_for_each_other()
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => InstallAsync(Acme)));

        await VerifyAsync(Acme);
    }

    [Fact]
    public async Task The_application_role_cannot_be_public()
    {
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            DatabaseLifecycle.InstallAsync([Widgets()], Acme, OpenAdminAsync, "public", cancellationToken: Ct));
        Assert.Contains("every role holds", exception.Message);
    }

    private async Task AdministerAsync(string sql)
    {
        await using var admin = await OpenAsync(_admin, Ct);
        await using var command = admin.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    // Row-level security does not bind a superuser: the application connected as one read every compartment, and the
    // verification passed.
    [Fact]
    public async Task The_application_connected_as_a_superuser_fails_the_verification()
    {
        await InstallAsync(Acme);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme, asApplication: false));
        Assert.Contains("a superuser, which row-level security does not bind", exception.Message);
    }

    [Fact]
    public async Task The_application_role_bypassing_owning_or_creating_fails_the_verification()
    {
        await InstallAsync(Acme);
        await AdministerAsync($"alter role {AppRole} bypassrls; alter table lcw.widget_tags owner to {AppRole}; grant create on schema lcw to {AppRole}");
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme));
            Assert.Contains($"connects as {AppRole}, which bypasses row-level security (BYPASSRLS)", exception.Message);
            Assert.Contains($"which owns widget_tags in lcw", exception.Message);
            Assert.Contains("which may create objects in lcw", exception.Message);
        }
        finally
        {
            await AdministerAsync($"alter role {AppRole} nobypassrls");
        }
    }

    // Policies add up: one more, permissive, opens what the declared ones keep closed — measured, the application of
    // acme then read every widget.
    [Fact]
    public async Task A_policy_not_declared_fails_the_verification()
    {
        await InstallAsync(Acme);
        await AdministerAsync("create policy pl_open on lcw.widgets using (true)");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme));
        Assert.Contains("the policy pl_open on lcw.widgets is not declared", exception.Message);
    }

    // A policy of the frame narrows: written permissive, it would widen what the space policy lets through.
    [Fact]
    public async Task A_policy_of_the_frame_written_permissive_fails_the_verification()
    {
        await InstallAsync(AcmeIsolated);
        await AdministerAsync("""
            drop policy pl_widgets_owner on lcw.widgets;
            create policy pl_widgets_owner on lcw.widgets using (created_by = current_setting('stellaegis.actor', true));
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(AcmeIsolated));
        Assert.Contains("the policy pl_widgets_owner on lcw.widgets follows the frame and is permissive", exception.Message);
    }

    // Installed by a superuser, a view reads its tables as its owner: measured, a view of acme returned the rows of
    // globex. Declared security_invoker, it reads as the application.
    [Fact]
    public async Task What_reads_around_row_level_security_fails_the_verification()
    {
        await InstallAsync(Acme);
        await AdministerAsync("""
            create view lcw.widget_names as select name from lcw.widgets;
            create view lcw.widget_names_invoker with (security_invoker = on) as select name from lcw.widgets;
            create materialized view lcw.widget_count as select count(*) from lcw.widgets;
            create function lcw.all_widgets() returns bigint language sql security definer as 'select count(*) from lcw.widgets';
            """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(Acme));
        Assert.Contains("the view lcw.widget_names reads as its owner", exception.Message);
        Assert.DoesNotContain("widget_names_invoker", exception.Message);
        Assert.Contains("the materialized view lcw.widget_count", exception.Message);
        Assert.Contains("the function lcw.all_widgets runs as an owner", exception.Message);
    }
}

public class ModuleDatabaseTests
{
    private sealed class WidgetsModule : IStellaegisModule
    {
        public static string Name => "widgets";
    }

    private sealed class GadgetsModule : IStellaegisModule
    {
        public static string Name => "gadgets";
    }

    [Fact]
    public void A_module_has_a_schema_of_its_own()
    {
        Assert.Throws<ArgumentException>(() => ModuleDatabase.PostgreSql<WidgetsModule>("public"));
        Assert.Throws<ArgumentException>(() => ModuleDatabase.SqlServer<WidgetsModule>("dbo"));
    }

    // Two modules on one schema would record their scripts in the same table, each reading the other's as applied with
    // another content; one declaration twice is said as such.
    [Fact]
    public async Task Two_declarations_on_one_schema_are_refused()
    {
        var widgets = ModuleDatabase.PostgreSql<WidgetsModule>("shared").WithScripts([new SqlScript(1, "w", "select 1")]);
        var gadgets = ModuleDatabase.PostgreSql<GadgetsModule>("shared").WithScripts([new SqlScript(1, "g", "select 1")]);
        Task<System.Data.Common.DbConnection> Never(ModuleDatabase _, CancellationToken __) => throw new InvalidOperationException("never opened");

        var shared = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseLifecycle.VerifyAsync([widgets, gadgets], ApplicationFrame.SingleCompartment("acme"), Never, TestContext.Current.CancellationToken));
        Assert.Contains("widgets and gadgets share the schema shared", shared.Message);

        var twice = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseLifecycle.InstallAsync([widgets, widgets], ApplicationFrame.SingleCompartment("acme"), Never, "app", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("The database of widgets is declared twice", twice.Message);
    }

    [Fact]
    public void The_embedded_scripts_are_read_in_order_with_a_stable_checksum()
    {
        var scripts = SqlScripts.FromResources(typeof(ModuleDatabaseTests).Assembly, "Stellaegis.Primitives.Tests.Data.Sql.PostgreSql");

        Assert.Equal(["0001_widgets.sql", "0002_widget_tags.sql"], scripts.Select(script => script.FileName));
        Assert.Equal(SqlScript.ChecksumOf(scripts[0].Sql.Replace("\n", "\r\n") + "\n\n"), scripts[0].Checksum);
        Assert.NotEqual(scripts[0].Checksum, scripts[1].Checksum);
        Assert.Throws<InvalidOperationException>(() => SqlScripts.FromResources(typeof(ModuleDatabaseTests).Assembly, "Nowhere"));
    }

    // A numbered script misnamed would never be installed, and the verification would pass: the scripts after it are
    // contiguous all the same, and it is usually the last.
    [Fact]
    public void A_misnamed_numbered_script_is_refused_rather_than_left_out()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SqlScripts.FromResources(typeof(ModuleDatabaseTests).Assembly, "Stellaegis.Primitives.Tests.Data.Sql.Misnamed"));

        Assert.Contains("0002_Add_Index.sql", exception.Message);
        Assert.DoesNotContain("0001_first.sql", exception.Message);
    }

    [Fact]
    public void The_versions_are_contiguous_from_1()
    {
        var exception = Assert.Throws<ArgumentException>(() => ModuleDatabase.PostgreSql<WidgetsModule>("lcw")
            .WithScripts([new SqlScript(1, "a", "select 1"), new SqlScript(3, "c", "select 3")]));

        Assert.Contains("expected version 2, found 0003_c.sql", exception.Message);
        Assert.Throws<ArgumentException>(() => new SqlScript(0, "a", "select 1"));
    }

    [Fact]
    public void Names_reaching_the_engine_are_identifiers()
    {
        Assert.Throws<ArgumentException>(() => ModuleDatabase.PostgreSql<WidgetsModule>("lcw-schema"));
        Assert.Throws<ArgumentException>(() => ModuleDatabase.PostgreSql<WidgetsModule>("lcw").RequiringExtension("pg crypto"));
        Assert.Throws<ArgumentException>(() => ModuleDatabase.PostgreSql<WidgetsModule>("lcw").WithRowSecurity("widgets", "pl_widgets"));
        Assert.Throws<ArgumentException>(() => new RowPolicy("lcw.widgets", "pl-widgets"));
        Assert.Equal("widgets (PostgreSql, schema lcw)", ModuleDatabase.PostgreSql<WidgetsModule>("lcw").ToString());
    }
}
