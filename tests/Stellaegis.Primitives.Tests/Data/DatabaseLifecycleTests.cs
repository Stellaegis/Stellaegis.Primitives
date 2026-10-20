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
        Assert.Contains("drop schema lcw cascade", installation.Message);
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
}

public class ModuleDatabaseTests
{
    private sealed class WidgetsModule : IStellaegisModule
    {
        public static string Name => "widgets";
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

    [Fact]
    public async Task SqlServer_is_declared_and_not_yet_written()
    {
        var database = ModuleDatabase.SqlServer<WidgetsModule>("lcw");

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() =>
            DatabaseLifecycle.VerifyAsync([database], ApplicationFrame.SingleCompartment("acme"), (_, _) => Task.FromResult<DbConnection>(null!)));
        Assert.Contains("SqlServer", exception.Message);
    }
}
