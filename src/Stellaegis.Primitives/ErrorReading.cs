using System.Net;
using System.Text;
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

    /// <summary>The largest body read as problem details: what an error says fits well below.</summary>
    public const int MaxProblemLength = 64 * 1024;

    /// <summary>The longest message read back, in characters; a longer <c>detail</c> is cut.</summary>
    public const int MaxMessageLength = 1024;

    /// <summary>The longest code read back, in characters; a longer <c>code</c> is not one.</summary>
    public const int MaxCodeLength = 128;

    /// <summary>
    /// The error of an answer, or <see langword="null"/> when its status is a success. Problem details give the code,
    /// from the <c>code</c> extension, and the message, from <c>detail</c>; the category comes from the status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A status without a category of its own — 422, 429, a 502 from a proxy — reads as <see cref="ErrorType.Failure"/>.
    /// So do 401 and 403: the call is the host's, made with its own credentials, and a refusal of them is a fault of the
    /// installation, which the person behind the request cannot mend by signing in again — their caller answers 500. The
    /// code is kept: a client whose service answers a refusal by design, as a decision does, still reads it.
    /// </para>
    /// <para>
    /// An answer that is not problem details, or whose <c>detail</c> was hidden, reads with no code and the reason
    /// phrase as its message: the body is not copied, since nothing says what it holds. The body comes from another
    /// service, and is read as such: at most <see cref="MaxProblemLength"/> bytes, as the UTF-8 that problem details
    /// are, its code and message stripped of control and format characters — no line break reaches a log — and cut.
    /// </para>
    /// </remarks>
    public static async Task<Error?> ReadErrorAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(response);
        if (response.IsSuccessStatusCode)
            return null;

        var type = ToErrorType(response.StatusCode);
        var reason = Clean(response.ReasonPhrase, MaxMessageLength) ?? $"HTTP {(int)response.StatusCode}";
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, ProblemMediaType, StringComparison.OrdinalIgnoreCase))
            return new Error(string.Empty, reason, type);

        var body = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (body is null)
            return new Error(string.Empty, reason, type);
        try
        {
            using var problem = JsonDocument.Parse(body);
            var code = Clean(StringProperty(problem.RootElement, "code"), int.MaxValue);
            return new Error(
                code is { Length: <= MaxCodeLength } ? code : string.Empty,
                Clean(StringProperty(problem.RootElement, "detail"), MaxMessageLength) ?? reason,
                type);
        }
        catch (JsonException)
        {
            return new Error(string.Empty, reason, type);
        }
    }

    // At most MaxProblemLength bytes; null beyond, as for a body that is not problem details.
    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxProblemLength)
            return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaxProblemLength + 1];
        var read = 0;
        int count;
        while (read < buffer.Length && (count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false)) > 0)
            read += count;
        return read > MaxProblemLength ? null : buffer[..read];
    }

    // A text from another service, as it may reach a log or a message: no control nor format character — line breaks,
    // bidirectional overrides, zero widths —, spaces collapsed, cut at the length given.
    private static string? Clean(string? text, int maxLength)
    {
        if (text is null)
            return null;
        var cleaned = new StringBuilder(Math.Min(text.Length, maxLength));
        for (var i = 0; i < text.Length && cleaned.Length < maxLength; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cleaned.Append(c).Append(text[++i]);
                continue;
            }
            if (char.IsSurrogate(c))
                continue;   // alone, no character at all
            var category = char.GetUnicodeCategory(c);
            if (category == System.Globalization.UnicodeCategory.Format)
                continue;
            if (char.IsWhiteSpace(c) || category == System.Globalization.UnicodeCategory.Control)
            {
                if (cleaned.Length > 0 && cleaned[^1] != ' ')
                    cleaned.Append(' ');
                continue;
            }
            cleaned.Append(c);
        }
        var result = cleaned.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    /// <summary>The inverse of the host's table, from a category to a status.</summary>
    private static ErrorType ToErrorType(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => ErrorType.Validation,
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
