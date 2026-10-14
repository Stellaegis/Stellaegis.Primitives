using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The single translation of errors to HTTP: <see cref="ErrorType"/> to status code, <see cref="Error"/> to
/// <see cref="ProblemDetails"/>, <see cref="Result"/> to <see cref="IResult"/>, and a filter for the exceptions
/// meant for the caller. A module uses it rather than writing its own.
/// </summary>
public static class ErrorTranslation
{
    /// <summary>The HTTP status of an error category.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is <see cref="ErrorType.None"/>, which is no error.</exception>
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only an error category has an HTTP status."),
    };

    /// <summary>
    /// The problem details of an error: its status, its message as <c>detail</c>, and its code as the
    /// <c>code</c> extension. <c>type</c> and <c>title</c> are left to ASP.NET Core's defaults for the status.
    /// </summary>
    /// <remarks>
    /// The message is copied as it is. <see cref="ToHttpResult(Result)"/> hides the message of a
    /// <see cref="ErrorType.Failure"/> outside Development; code writing these details itself decides.
    /// </remarks>
    public static ProblemDetails ToProblemDetails(this Error error)
    {
        Guard.NotNull(error);
        var details = new ProblemDetails
        {
            Status = error.Type.ToStatusCode(),
            Detail = error.Message,
        };
        details.Extensions["code"] = error.Code;
        return details;
    }

    /// <summary>
    /// The HTTP answer of a result: 204 on success, problem details on failure. The message of a
    /// <see cref="ErrorType.Failure"/> is hidden outside Development — it usually describes a technical cause —
    /// and its code is kept.
    /// </summary>
    public static IResult ToHttpResult(this Result result)
        => Guard.NotNull(result).IsSuccess ? TypedResults.NoContent() : new ErrorResult(result.Error!);

    /// <summary>
    /// The HTTP answer of a result: 200 with the value on success, problem details on failure, as
    /// <see cref="ToHttpResult(Result)"/>.
    /// </summary>
    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
        => Guard.NotNull(result).IsSuccess ? TypedResults.Ok(result.Value) : new ErrorResult(result.Error!);

    /// <summary>
    /// Translates, on these routes, the exceptions meant for the caller: a <see cref="UserFacingException"/>
    /// answers the status of its <see cref="ErrorType"/> with its message; an <see cref="ArgumentException"/>
    /// answers 400, its message hidden outside Development since a guard names an internal parameter. Any other
    /// exception goes on to the host's exception handler.
    /// </summary>
    /// <remarks>An ordinary convention: a filter added by a final convention is ignored by ASP.NET Core.</remarks>
    public static TBuilder AddErrorTranslation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter(async (context, next) =>
        {
            try
            {
                return await next(context);
            }
            catch (UserFacingException exception)
            {
                return Problem(exception.Type.ToStatusCode(), exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Problem(StatusCodes.Status400BadRequest, IsDevelopment(context.HttpContext) ? exception.Message : null);
            }
        });
    }

    private static IResult Problem(int status, string? detail)
        => TypedResults.Problem(new ProblemDetails { Status = status, Detail = detail });

    private static bool IsDevelopment(HttpContext httpContext)
        => httpContext.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true;

    /// <summary>A failed result, translated when it executes, once the environment is known.</summary>
    private sealed class ErrorResult(Error error) : IResult, IStatusCodeHttpResult
    {
        public int? StatusCode => error.Type.ToStatusCode();

        public Task ExecuteAsync(HttpContext httpContext)
        {
            var details = error.ToProblemDetails();
            if (error.Type == ErrorType.Failure && !IsDevelopment(httpContext))
                details.Detail = null;
            return TypedResults.Problem(details).ExecuteAsync(httpContext);
        }
    }
}
