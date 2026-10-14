using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Stellaegis.Primitives.Data;

namespace Stellaegis.Primitives.Tests.Data;

public class PrimitiveTypeHandlersTests
{
    private sealed class DocumentRow
    {
        public Compartment? Compartment { get; set; }
        public Actor? CreatedBy { get; set; }
    }

    private sealed record DocumentRecord(Compartment Compartment, Actor CreatedBy);

    static PrimitiveTypeHandlersTests()
    {
        PrimitiveTypeHandlers.Register();
        SnakeCaseTypeMap<DocumentRow>.Register();
        SnakeCaseTypeMap<DocumentRecord>.Register();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(Ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE documents (compartment TEXT, created_by TEXT)", cancellationToken: Ct));
        return connection;
    }

    [Fact]
    public async Task Writes_and_reads_settable_properties()
    {
        await using var connection = await OpenAsync();

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO documents VALUES (@compartment, @createdBy)",
            new { compartment = Compartment.Parse("acme"), createdBy = Actor.Parse("job:purge") },
            cancellationToken: Ct));
        var row = await connection.QuerySingleAsync<DocumentRow>(new CommandDefinition(
            "SELECT compartment, created_by FROM documents", cancellationToken: Ct));

        Assert.Equal(Compartment.Parse("acme"), row.Compartment);
        Assert.Equal(Actor.Parse("job:purge"), row.CreatedBy);
    }

    [Fact]
    public async Task Reads_positional_records_through_the_snake_case_map()
    {
        await using var connection = await OpenAsync();

        var row = await connection.QuerySingleAsync<DocumentRecord>(new CommandDefinition(
            "SELECT 'acme' AS compartment, 'jane' AS created_by", cancellationToken: Ct));

        Assert.Equal(new DocumentRecord(Compartment.Parse("acme"), Actor.Parse("jane")), row);
    }

    [Fact]
    public async Task Filters_on_a_parameter()
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO documents VALUES ('acme', 'jane'), ('other', 'jane')", cancellationToken: Ct));

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM documents WHERE compartment = @compartment",
            new { compartment = Compartment.Parse("acme") }, cancellationToken: Ct));

        Assert.Equal(1, count);
    }

    // Dapper expands a list parameter without its type handlers: the elements go to the provider as they
    // are, and it rejects them. A list is passed as its values. If this test fails, Dapper has changed and
    // the documented workaround can go.
    [Fact]
    public async Task A_list_parameter_is_passed_as_its_values()
    {
        await using var connection = await OpenAsync();
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO documents VALUES ('acme', 'jane'), ('other', 'jane'), ('third', 'jane')",
            cancellationToken: Ct));
        Compartment[] compartments = [Compartment.Parse("acme"), Compartment.Parse("third")];
        const string sql = "SELECT COUNT(*) FROM documents WHERE compartment IN @compartments";

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { compartments }, cancellationToken: Ct)));
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            sql, new { compartments = compartments.Select(c => c.Value) }, cancellationToken: Ct));

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Null_round_trips()
    {
        await using var connection = await OpenAsync();

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO documents VALUES (@compartment, @createdBy)",
            new { compartment = (Compartment?)null, createdBy = (Actor?)null }, cancellationToken: Ct));
        var row = await connection.QuerySingleAsync<DocumentRow>(new CommandDefinition(
            "SELECT compartment, created_by FROM documents", cancellationToken: Ct));

        Assert.Null(row.Compartment);
        Assert.Null(row.CreatedBy);
    }

    [Fact]
    public async Task An_invalid_stored_value_fails_the_read()
    {
        await using var connection = await OpenAsync();

        var exception = await Assert.ThrowsAsync<DataException>(() => connection.QuerySingleAsync<DocumentRow>(
            new CommandDefinition("SELECT 'Acme' AS compartment, 'jane' AS created_by", cancellationToken: Ct)));

        Assert.IsType<FormatException>(exception.InnerException);
        Assert.Contains("compartment=Acme", exception.Message);
    }

    [Fact]
    public void A_parameter_is_shaped_like_a_string_parameter()
    {
        var parameter = new SqliteParameter();

        new PrimitiveTypeHandlers.CompartmentTypeHandler().SetValue(parameter, Compartment.Parse("acme"));

        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(DbString.DefaultLength, parameter.Size);
        Assert.Equal("acme", parameter.Value);
    }
}
