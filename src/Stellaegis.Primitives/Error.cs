namespace Stellaegis.Primitives;

/// <summary>
/// Represents a typed, categorized error. Immutable value object: two errors are equal when their
/// <see cref="Code"/> and <see cref="Type"/> match — the <see cref="Message"/> is not compared.
/// </summary>
public sealed class Error(string code, string message, ErrorType type = ErrorType.Failure) : IEquatable<Error>
{
    /// <summary>No error — equivalent to success.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.None);

    /// <summary>Error code (e.g. <c>"User.NotFound"</c>).</summary>
    public string Code { get; } = code;

    /// <summary>Human-readable error message.</summary>
    public string Message { get; } = message;

    /// <summary>Error category.</summary>
    public ErrorType Type { get; } = type;

    /// <summary>Creates a generic failure error, typically wrapping an unexpected technical error.</summary>
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    /// <summary>Creates a validation error.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>Creates a not-found error.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>Creates a conflict error.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>Creates an unauthorized error.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>Creates a forbidden error.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>Creates a precondition failed error.</summary>
    public static Error PreconditionFailed(string code, string message) => new(code, message, ErrorType.PreconditionFailed);

    /// <inheritdoc/>
    public bool Equals(Error? other) => other is not null && Code == other.Code && Type == other.Type;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Error e && Equals(e);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Code, Type);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Error? left, Error? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Error? left, Error? right) => !(left == right);

    /// <inheritdoc/>
    public override string ToString() => $"{Type}:{Code} — {Message}";
}
