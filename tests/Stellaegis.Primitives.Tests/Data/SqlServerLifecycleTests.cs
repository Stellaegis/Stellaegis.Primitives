using System.Data.Common;
using Microsoft.Data.SqlClient;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>
/// The life cycle on a real SQL Server, named by <c>STELLAEGIS_TEST_SQLSERVER</c> and skipped without it: the database
/// <c>stellaegis_lcw</c> is dropped and created again by every test, under the login of the variable. The application
/// is the user <c>lcw_probe</c>, without login, member of the role the installation creates; its connection takes
/// that identity by <c>EXECUTE AS USER</c>, so that the tests need no SQL login and run on an instance that accepts
/// integrated security only.
/// </summary>
public sealed class SqlServerLifecycleTests : IAsyncLifetime
{
    private const string Database = "stellaegis_lcw";
    private const string Schema = "lcw";
    private const string AppRole = "lcw_app";
    private const string AppUser = "lcw_probe";
    private const string ResourcePrefix = "Stellaegis.Primitives.Tests.Data.Sql.SqlServer";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class WidgetsModule : IStellaegisModule
    {
        public static string Name => "widgets";
    }

    private static readonly ApplicationFrame Acme = ApplicationFrame.SingleCompartment("acme");

    private static readonly System.Reflection.Assembly Tests = typeof(SqlServerLifecycleTests).Assembly;

    private static ModuleDatabase Widgets(IEnumerable<SqlScript>? scripts = null) => ModuleDatabase.SqlServer<WidgetsModule>(Schema)
        .WithScripts(scripts ?? SqlScripts.FromResources(Tests, ResourcePrefix));

    private string _server = null!;
    private string _admin = null!;

    public async ValueTask InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable("STELLAEGIS_TEST_SQLSERVER");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), "STELLAEGIS_TEST_SQLSERVER is not set.");
        // No pooling: a connection that took the identity of the application must not serve the next test.
        _server = new SqlConnectionStringBuilder(value) { InitialCatalog = "master", Pooling = false }.ConnectionString;
        _admin = new SqlConnectionStringBuilder(_server) { InitialCatalog = Database }.ConnectionString;

        await DropDatabaseAsync();
        await using (var connection = new SqlConnection(_server))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"create database [{Database}]");
        }
        await using (var connection = new SqlConnection(_admin))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"create user [{AppUser}] without login");
        }
    }

    // The instance of a developer keeps nothing of the tests.
    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
            await DropDatabaseAsync();
    }

    private async Task DropDatabaseAsync()
    {
        await using var connection = new SqlConnection(_server);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, $"""
            if db_id(N'{Database}') is not null
            begin
              alter database [{Database}] set single_user with rollback immediate;
              drop database [{Database}];
            end
            """);
    }

    private async Task<DbConnection> OpenAdminAsync(ModuleDatabase _, CancellationToken ct)
    {
        var connection = new SqlConnection(_admin);
        await connection.OpenAsync(ct);
        return connection;
    }

    // The host's part: its user joins the role the installation created, then the connection runs as that user.
    private async Task<DbConnection> OpenAppAsync(ModuleDatabase _, CancellationToken ct)
    {
        var connection = new SqlConnection(_admin);
        await connection.OpenAsync(ct);
        await ExecuteAsync(connection, $"""
            if database_principal_id(N'{AppRole}') is not null and is_rolemember(N'{AppRole}', N'{AppUser}') = 0
              alter role [{AppRole}] add member [{AppUser}];
            execute as user = N'{AppUser}';
            """);
        return connection;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }

    private Task<InstallationReport> InstallAsync(ModuleDatabase? database = null)
        => DatabaseLifecycle.InstallAsync([database ?? Widgets()], Acme, OpenAdminAsync, AppRole, cancellationToken: Ct);

    private Task VerifyAsync(ModuleDatabase? database = null, bool asApplication = true)
        => DatabaseLifecycle.VerifyAsync([database ?? Widgets()], Acme, asApplication ? OpenAppAsync : OpenAdminAsync, Ct);

    [Fact]
    public async Task The_installation_applies_the_scripts_once_and_the_application_verifies_them()
    {
        var first = await InstallAsync();
        Assert.Equal(["widgets: 0001_widgets.sql", "widgets: 0002_widget_tags.sql"], first.Applied);
        Assert.Empty(first.Skipped);

        var again = await InstallAsync();
        Assert.Empty(again.Applied);
        Assert.Equal(["widgets: 0001_widgets.sql", "widgets: 0002_widget_tags.sql"], again.Skipped);

        await VerifyAsync();

        // The application writes data in the schema, objects created by a later script included, reads the record of
        // the scripts and cannot touch it.
        await using var app = await OpenAppAsync(Widgets(), Ct);
        await ExecuteAsync(app, "insert into lcw.widgets (name, created_by) values (N'w', N'u:1')");
        Assert.Equal(1, await ScalarAsync(app, "select count(*) from lcw.v_widget_names"));
        Assert.Equal("job:install", await ScalarAsync(app, "select max(applied_by) from lcw._schema_migrations"));
        await Assert.ThrowsAsync<SqlException>(() =>
            ExecuteAsync(app, "insert into lcw._schema_migrations (version, script_name, checksum, applied_by) values (9, N'x', N'x', N'x')"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(app, "create table lcw.intruder (id int not null)"));
    }

    [Fact]
    public async Task A_script_that_fails_leaves_nothing_behind()
    {
        var scripts = SqlScripts.FromResources(Tests, ResourcePrefix);
        var failing = Widgets([scripts[0], new SqlScript(2, "widget_tags", scripts[1].Sql + "\ngo\nthrow 50000, N'the second batch fails', 1;")]);

        await Assert.ThrowsAsync<SqlException>(() => InstallAsync(failing));

        await using var admin = await OpenAdminAsync(failing, Ct);
        Assert.Equal(DBNull.Value, await ScalarAsync(admin, "select object_id(N'lcw.widget_tags', N'U')"));
        Assert.Equal(1, await ScalarAsync(admin, "select count(*) from lcw._schema_migrations"));
        await InstallAsync();
        await VerifyAsync();
    }

    [Fact]
    public async Task A_script_applied_with_another_content_fails_the_verification_and_the_installation()
    {
        await InstallAsync();
        var scripts = SqlScripts.FromResources(Tests, ResourcePrefix);
        var changed = Widgets([new SqlScript(1, scripts[0].Name, scripts[0].Sql + "\n-- corrected"), scripts[1]]);

        var verification = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(changed));
        Assert.Contains("widgets: 0001_widgets.sql was applied with another content", verification.Message);

        var installation = await Assert.ThrowsAsync<InvalidOperationException>(() => InstallAsync(changed));
        Assert.Contains("recreate the database", installation.Message);
    }

    [Fact]
    public async Task A_database_not_installed_names_the_installation()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(asApplication: false));

        Assert.Contains("widgets is not installed: lcw._schema_migrations does not exist", exception.Message);
        Assert.Contains("DatabaseLifecycle.InstallAsync", exception.Message);
    }

    [Fact]
    public async Task A_database_ahead_of_the_package_fails_the_verification()
    {
        await InstallAsync();
        var behind = Widgets([SqlScripts.FromResources(Tests, ResourcePrefix)[0]]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync(behind));
        Assert.Contains("holds version 2 (widget_tags), which this package does not know", exception.Message);
    }

    [Fact]
    public async Task The_application_without_its_rights_on_the_schema_does_not_see_the_installation()
    {
        await InstallAsync();
        await using (var admin = await OpenAdminAsync(Widgets(), Ct))
            await ExecuteAsync(admin, $"revoke select, insert, update, delete, execute, view definition on schema::[{Schema}] from [{AppRole}]");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => VerifyAsync());
        Assert.Contains("widgets is not installed", exception.Message);
    }
}

public class SqlServerBatchesTests
{
    [Fact]
    public void A_script_is_split_on_the_lines_holding_only_go()
    {
        var batches = SqlServerLifecycle.Batches("create table a (x int);\r\ngo\r\n\r\ncreate view b as select 'go' as g;\n  GO  \n\ngo\nselect 1 -- go on\n");

        Assert.Equal(["create table a (x int);", "create view b as select 'go' as g;", "select 1 -- go on"], batches);
    }

    private sealed class WidgetsModule : IStellaegisModule
    {
        public static string Name => "widgets";
    }

    [Fact]
    public void What_SqlServer_does_not_declare_yet_is_refused_at_the_declaration()
    {
        var database = ModuleDatabase.SqlServer<WidgetsModule>("lcw");

        Assert.Contains("no extensions", Assert.Throws<NotSupportedException>(() => database.RequiringExtension("pgcrypto")).Message);
        Assert.Contains("one enabled security policy", Assert.Throws<NotSupportedException>(() => database.WithRowSecurity("lcw.widgets", "pl_widgets_space")).Message);
        Assert.Throws<NotSupportedException>(() => database.WhenFrameDeclares(FrameFeature.OwnerIsolation, "a", "select 1", "r", "select 1"));
        Assert.Equal("widgets (SqlServer, schema lcw)", database.ToString());
    }
}
