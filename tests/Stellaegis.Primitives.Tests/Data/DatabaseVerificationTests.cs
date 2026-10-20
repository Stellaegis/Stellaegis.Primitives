using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Stellaegis.Primitives.Data;
using Stellaegis.Primitives.Hosting;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>
/// The verification of the module databases by a host, against a real PostgreSQL named by
/// <c>STELLAEGIS_TEST_POSTGRESQL</c> and skipped without it. Its own schema, <c>lcv</c>, and its own application
/// role, <c>lcv_app</c>, so that it never meets the life-cycle tests running beside it.
/// </summary>
public sealed class DatabaseVerificationTests : IAsyncLifetime
{
    private const string Schema = "lcv";
    private const string AppRole = "lcv_app";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class GadgetsModule : IStellaegisModule
    {
        public static string Name => "gadgets";
    }

    private static readonly ApplicationFrame Acme = ApplicationFrame.SingleCompartment("acme");

    private static readonly ModuleDatabase Gadgets = ModuleDatabase.PostgreSql<GadgetsModule>(Schema)
        .WithScripts([new SqlScript(1, "gadgets", $"create table {Schema}.gadgets (name text not null)")]);

    private string _connectionString = null!;

    public async ValueTask InitializeAsync()
    {
        var value = Environment.GetEnvironmentVariable("STELLAEGIS_TEST_POSTGRESQL");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), "STELLAEGIS_TEST_POSTGRESQL is not set.");
        _connectionString = value!;
        await using var connection = new NpgsqlConnection(_connectionString);
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

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services
            .AddStellaegisFrame(Acme)
            .AddStellaegisModule(ModuleDeclaration.Compartmented<GadgetsModule>())
            .AddStellaegisModuleDatabase(Gadgets, (_, ct) => OpenAsync(ct))
            .AddStellaegisDatabaseVerification()
            .AddStellaegisDatabaseVerification();
        return builder.Build();
    }

    [Fact]
    public async Task The_host_fails_until_the_database_is_installed_and_starts_once_it_is()
    {
        using (var host = BuildHost())
        {
            Assert.Single(host.Services.GetServices<IHostedService>(), service => service is DatabaseVerificationHostedService);
            Assert.Equal([Gadgets], host.Services.GetStellaegisModuleDatabases());

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync(Ct));
            Assert.Contains("gadgets is not installed", exception.Message);
        }

        await DatabaseLifecycle.InstallAsync([Gadgets], Acme, (_, ct) => OpenAsync(ct), AppRole, cancellationToken: Ct);

        using (var host = BuildHost())
        {
            await host.StartAsync(Ct);
            await host.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task An_application_without_a_module_database_has_nothing_to_verify()
    {
        using var provider = new ServiceCollection().AddStellaegisFrame(Acme).BuildServiceProvider();

        await provider.VerifyStellaegisDatabasesAsync(Ct);
        Assert.Empty(provider.GetStellaegisModuleDatabases());
    }
}
