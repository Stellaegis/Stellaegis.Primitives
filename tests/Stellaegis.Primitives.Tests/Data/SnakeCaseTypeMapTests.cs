using Dapper;
using Microsoft.Data.Sqlite;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

public class SnakeCaseTypeMapTests
{
    private sealed class PartyRow
    {
        [ColumnMap("party_unique_id")]
        public string UniqueId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string MfaSecret { get; set; } = string.Empty;
    }

    private sealed record PartyRecord(string UniqueId, string DisplayName);

    private sealed record OperationRecord([property: ColumnMap("operation_unique_id")] string UniqueId, string Status);

    static SnakeCaseTypeMapTests()
    {
        SqlMapper.SetTypeMap(typeof(PartyRow), new SnakeCaseTypeMap<PartyRow>());
        SnakeCaseTypeMap<PartyRecord>.Register();
        SnakeCaseTypeMap<OperationRecord>.Register();
    }

    [Theory]
    [InlineData("DisplayName", "display_name")]
    [InlineData("Id", "id")]
    [InlineData("MFA", "mfa")]
    [InlineData("MfaSecret", "mfa_secret")]
    [InlineData("HTTPStatus", "http_status")]
    [InlineData("AddressLine2", "address_line2")]
    [InlineData("Line2Text", "line2_text")]
    public void Converts_pascal_case_to_snake_case(string property, string column)
    {
        Assert.Equal(column, SnakeCaseTypeMap<PartyRow>.ToSnakeCase(property));
    }

    [Fact]
    public async Task Maps_settable_properties_with_convention_and_override()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        var row = await connection.QuerySingleAsync<PartyRow>(new CommandDefinition(
            "SELECT 'p1' AS party_unique_id, 'Acme' AS display_name, 's' AS mfa_secret",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("p1", row.UniqueId);
        Assert.Equal("Acme", row.DisplayName);
        Assert.Equal("s", row.MfaSecret);
    }

    [Fact]
    public async Task Maps_positional_records()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        var row = await connection.QuerySingleAsync<PartyRecord>(new CommandDefinition(
            "SELECT 'p1' AS unique_id, 'Acme' AS display_name",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("p1", row.UniqueId);
        Assert.Equal("Acme", row.DisplayName);
    }

    [Fact]
    public async Task Maps_a_column_override_on_a_record_property()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        var row = await connection.QuerySingleAsync<OperationRecord>(new CommandDefinition(
            "SELECT 'o1' AS operation_unique_id, 'done' AS status",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("o1", row.UniqueId);
        Assert.Equal("done", row.Status);
    }
}
