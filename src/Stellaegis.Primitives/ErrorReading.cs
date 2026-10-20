using System.Net;
using System.Text.Json;

namespace Stellaegis.Primitives;

/// <summary>
/// The client side of the error translation: reads back, from an HTTP answer, the <see cref="Error"/> that a host
/// using <c>ErrorTranslation</c> (Stellaegis.Primitives.AspNetCore) wrote as problem details. A module's client
/// recognizes its managed exceptions by their code, and raises them as they are raised in process.
/// </summary>
public static class ErrorReading
{
    private const string ProblemMediaType = "application/problem+json";

    /// <summary>
    /// The error of an answer, or <see langword="null"/> when its status is a success. Problem details give the code,
    /// from the <c>code</c> extension, and the message, from <c>detail</c>; the category comes from the status.
    /// </summary>
    /// <remarks>
    /// A status without a category of its own — 422, 429, a 502 from a proxy — reads as <see cref="ErrorType.Failure"/>.
    /// An answer that is not problem details, or whose <c>detail</c> was hidden, reads with no code and the reason
    /// phrase as its message: the body is not copied, since nothing says what it holds.
    /// </remarks>
    public static async Task<Error?> ReadErrorAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(response);
        if (response.IsSuccessStatusCode)
            return null;

        var type = ToErrorType(response.StatusCode);
        var reason = response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}";
        if (response.Content.Headers.ContentType?.MediaType != ProblemMediaType)
            return new Error(string.Empty, reason, type);

        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return new Error(
                StringProperty(problem.RootElement, "code") ?? string.Empty,
                StringProperty(problem.RootElement, "detail") ?? reason,
                type);
        }
        catch (JsonException)
        {
            return new Error(string.Empty, reason, type);
        }
    }

    /// <summary>The inverse of the host's table, from a category to a status.</summary>
    private static ErrorType ToErrorType(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => ErrorType.Validation,
        HttpStatusCode.Unauthorized => ErrorType.Unauthorized,
        HttpStatusCode.Forbidden => ErrorType.Forbidden,
        HttpStatusCode.NotFound => ErrorType.NotFound,
        HttpStatusCode.Conflict => ErrorType.Conflict,
        HttpStatusCode.PreconditionFailed => ErrorType.PreconditionFailed,
        _ => ErrorType.Failure,
    };

    private static string? StringProperty(JsonElement problem, string name)
        => problem.ValueKind == JsonValueKind.Object
            && problem.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}
