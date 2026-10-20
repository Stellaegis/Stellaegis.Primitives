using System.Net;
using System.Text;

namespace Stellaegis.Primitives.Tests;

public class ErrorReadingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_success_reads_no_error()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.Null(await response.ReadErrorAsync(Ct));
    }

    [Fact]
    public async Task Problem_details_give_code_and_message()
    {
        using var response = Problem(HttpStatusCode.NotFound, """{"status":404,"detail":"Note jane-1 does not exist.","code":"Note.NotFound"}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.NotNull(error);
        Assert.Equal("Note.NotFound", error.Code);
        Assert.Equal("Note jane-1 does not exist.", error.Message);
        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    [Fact]
    public async Task A_hidden_detail_reads_as_the_reason_phrase()
    {
        using var response = Problem(HttpStatusCode.InternalServerError, """{"status":500,"code":"Db.Timeout"}""");

        var error = await response.ReadErrorAsync(Ct);

        Assert.NotNull(error);
        Assert.Equal("Db.Timeout", error.Code);
        Assert.Equal("Internal Server Error", error.Message);
        Assert.Equal(ErrorType.Failure, error.Type);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task A_status_without_category_reads_as_a_failure(HttpStatusCode status)
    {
        using var response = Problem(status, """{"code":"Some.Code"}""");

        Assert.Equal(ErrorType.Failure, (await response.ReadErrorAsync(Ct))!.Type);
    }

    [Fact]
    public async Task An_answer_that_is_not_problem_details_is_not_copied()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>upstream 10.0.0.4 refused</html>", Encoding.UTF8, "text/html"),
        };

        var error = await response.ReadErrorAsync(Ct);

        Assert.NotNull(error);
        Assert.Equal(string.Empty, error.Code);
        Assert.Equal("Bad Gateway", error.Message);
    }

    [Fact]
    public async Task Malformed_problem_details_read_without_code()
    {
        using var response = Problem(HttpStatusCode.BadRequest, "not json");

        var error = await response.ReadErrorAsync(Ct);

        Assert.NotNull(error);
        Assert.Equal(string.Empty, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
    };
}
