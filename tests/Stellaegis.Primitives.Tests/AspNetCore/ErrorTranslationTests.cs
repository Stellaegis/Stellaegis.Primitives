using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Stellaegis.Primitives.AspNetCore;

namespace Stellaegis.Primitives.Tests.AspNetCore;

public class ErrorTranslationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ErrorType.Failure, 500)]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.PreconditionFailed, 412)]
    public void Each_category_has_its_status(ErrorType type, int status)
        => Assert.Equal(status, type.ToStatusCode());

    [Fact]
    public void None_is_no_error_and_has_no_status()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ErrorType.None.ToStatusCode());

    [Fact]
    public void Problem_details_carry_status_message_and_code()
    {
        var details = Error.NotFound("Note.NotFound", "Note jane-1 does not exist.").ToProblemDetails();

        Assert.Equal(404, details.Status);
        Assert.Equal("Note jane-1 does not exist.", details.Detail);
        Assert.Equal("Note.NotFound", details.Extensions["code"]);
        Assert.Null(details.Type);
    }

    [Fact]
    public async Task A_successful_result_with_a_value_answers_200()
    {
        var (status, body) = await AnswerAsync(() => Result.Success("jane-1").ToHttpResult());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("jane-1", body.GetString());
    }

    [Fact]
    public async Task A_successful_result_without_value_answers_204()
    {
        await using var app = await StartAsync(() => Result.Success().ToHttpResult());

        Assert.Equal(HttpStatusCode.NoContent, (await app.GetTestClient().GetAsync("/", Ct)).StatusCode);
    }

    [Fact]
    public async Task A_failed_result_answers_problem_details()
    {
        var (status, body) = await AnswerAsync(() =>
            Result.Failure<string>(Error.Conflict("Note.Exists", "A note with this id exists.")).ToHttpResult());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(409, body.GetProperty("status").GetInt32());
        Assert.Equal("A note with this id exists.", body.GetProperty("detail").GetString());
        Assert.Equal("Note.Exists", body.GetProperty("code").GetString());
        Assert.StartsWith("https://", body.GetProperty("type").GetString());
        Assert.Equal("Conflict", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_message_of_a_failure_is_hidden_outside_development()
    {
        var (status, body) = await AnswerAsync(() => Result.Failure(Error.Failure("Db.Timeout", "Timeout on 10.0.0.4.")).ToHttpResult());

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.False(body.TryGetProperty("detail", out _));
        Assert.Equal("Db.Timeout", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_message_of_a_failure_shows_in_development()
    {
        var (_, body) = await AnswerAsync(
            () => Result.Failure(Error.Failure("Db.Timeout", "Timeout on 10.0.0.4.")).ToHttpResult(), "Development");

        Assert.Equal("Timeout on 10.0.0.4.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_user_facing_exception_answers_its_status_and_message()
    {
        var (status, body) = await AnswerAsync(string () => throw new UserFacingException("This note is locked.", ErrorType.Conflict));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("This note is locked.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_argument_exception_answers_400_without_its_message()
    {
        var (status, body) = await AnswerAsync(() => Guard.NotWhiteSpace(" "));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(body.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task An_argument_exception_shows_its_message_in_development()
    {
        var (_, body) = await AnswerAsync(() => Guard.NotNull<string>(null), "Development");

        Assert.Contains("null", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Any_other_exception_goes_on_to_the_host()
    {
        await using var app = await StartAsync(() => Compartment.Parse("Acme"));

        await Assert.ThrowsAsync<FormatException>(() => app.GetTestClient().GetAsync("/", Ct));
    }

    private static Task<WebApplication> StartAsync(Delegate handler, string environment = "Production")
        => TestApp.StartAsync(_ => { }, routes => routes.MapGet("/", handler).AddErrorTranslation(), environment);

    private static async Task<(HttpStatusCode Status, JsonElement Body)> AnswerAsync(Delegate handler, string environment = "Production")
    {
        await using var app = await StartAsync(handler, environment);
        var response = await app.GetTestClient().GetAsync("/", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return (response.StatusCode, JsonDocument.Parse(body).RootElement.Clone());
    }
}
