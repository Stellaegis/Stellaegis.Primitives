using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Stellaegis.Primitives.Tests.Data;

/// <summary>
/// What each engine does with a session context when a pooled connection is reused — behaviour of the engines
/// and their drivers that the unit of work relies on rather than codes. A pool of one connection forces the
/// reuse. The tests run against real engines, named by environment variables, and are skipped without them:
/// <c>STELLAEGIS_TEST_POSTGRESQL</c> and <c>STELLAEGIS_TEST_SQLSERVER</c>, connection strings.
/// </summary>
/// <remarks>
/// Measured on 2026-10-17. Npgsql resets a pooled connection unless the connection string sets
/// <c>No Reset On Close=true</c>, with which the PostgreSQL test fails: the setting is inherited. Microsoft.Data.SqlClient
/// rejects <c>Connection Reset=false</c> on .NET, so a SQL Server connection is always reset.
/// </remarks>
public class SessionContextPoolTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ConnectionString(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(value), $"{variable} is not set.");
        return value!;
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(Ct);
    }

    [Fact]
    public async Task PostgreSql_does_not_hand_a_session_setting_to_the_next_user_of_a_pooled_connection()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString("STELLAEGIS_TEST_POSTGRESQL"))
        {
            MaxPoolSize = 1,
            ApplicationName = $"pool-{Guid.NewGuid():N}",
        };

        int firstBackend;
        await using (var first = new NpgsqlConnection(builder.ConnectionString))
        {
            await first.OpenAsync(Ct);
            firstBackend = Convert.ToInt32(await ScalarAsync(first, "select pg_backend_pid()"));
            await ScalarAsync(first, "select set_config('stellaegis.space', 'acme', false)");
            Assert.Equal("acme", await ScalarAsync(first, "select current_setting('stellaegis.space', true)"));
        }

        await using var second = new NpgsqlConnection(builder.ConnectionString);
        await second.OpenAsync(Ct);
        Assert.Equal(firstBackend, Convert.ToInt32(await ScalarAsync(second, "select pg_backend_pid()")));
        var inherited = await ScalarAsync(second, "select current_setting('stellaegis.space', true)");
        Assert.True(inherited is null or DBNull || (string)inherited == string.Empty, $"Inherited: '{inherited}'.");
    }

    [Fact]
    public async Task SqlServer_does_not_hand_a_session_context_to_the_next_user_of_a_pooled_connection()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString("STELLAEGIS_TEST_SQLSERVER"))
        {
            MaxPoolSize = 1,
            ApplicationName = $"pool-{Guid.NewGuid():N}",
        };

        short firstSession;
        await using (var first = new SqlConnection(builder.ConnectionString))
        {
            await first.OpenAsync(Ct);
            firstSession = Convert.ToInt16(await ScalarAsync(first, "select @@SPID"));
            await ScalarAsync(first, "exec sp_set_session_context N'stellaegis.space', N'acme', @read_only = 1");
            Assert.Equal("acme", await ScalarAsync(first, "select cast(session_context(N'stellaegis.space') as nvarchar(63))"));
        }

        await using var second = new SqlConnection(builder.ConnectionString);
        await second.OpenAsync(Ct);
        Assert.Equal(firstSession, Convert.ToInt16(await ScalarAsync(second, "select @@SPID")));
        Assert.IsType<DBNull>(await ScalarAsync(second, "select session_context(N'stellaegis.space')"));

        // Read-only for the session, not for the physical connection: the next user can set it again.
        await ScalarAsync(second, "exec sp_set_session_context N'stellaegis.space', N'globex', @read_only = 1");
        Assert.Equal("globex", await ScalarAsync(second, "select cast(session_context(N'stellaegis.space') as nvarchar(63))"));
    }
}
