namespace Stellaegis.Primitives;

/// <summary>Error categories used by <see cref="Error"/>, <see cref="Result"/> and <see cref="UserFacingException"/>.</summary>
public enum ErrorType
{
    /// <summary>No error (success).</summary>
    None = 0,
    /// <summary>Generic failure (HTTP 500).</summary>
    Failure = 1,
    /// <summary>Validation error (HTTP 400).</summary>
    Validation = 2,
    /// <summary>Resource not found (HTTP 404).</summary>
    NotFound = 3,
    /// <summary>Conflict with current state (HTTP 409).</summary>
    Conflict = 4,
    /// <summary>Unauthorized (HTTP 401).</summary>
    Unauthorized = 5,
    /// <summary>Forbidden (HTTP 403).</summary>
    Forbidden = 6,
    /// <summary>Precondition failed (HTTP 412), e.g. a failed ETag / If-Match concurrency check.</summary>
    PreconditionFailed = 7
}
