namespace Stellaegis.Primitives;

/// <summary>
/// Exception whose <see cref="Exception.Message"/> is safe to display to the end user,
/// regardless of the environment (Development or Production).
/// </summary>
/// <remarks>
/// Use this exception when a <see cref="Result{TValue}"/> return is not practical and you need to
/// communicate a business error to the caller. It carries an <see cref="ErrorType"/>, the same category
/// as <see cref="Error"/>: translating it to a transport status — an HTTP code, for instance — is the
/// host's job.
/// </remarks>
/// <example>
/// <code>throw new UserFacingException("Invoice amount must be greater than zero.");</code>
/// </example>
public class UserFacingException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserFacingException"/> with a user-safe message.
    /// </summary>
    /// <param name="message">A message safe to display to the end user.</param>
    /// <param name="type">Error category. Defaults to <see cref="ErrorType.Validation"/>; <see cref="ErrorType.None"/> is rejected.</param>
    /// <param name="innerException">The exception that caused this error, if any.</param>
    public UserFacingException(string message, ErrorType type = ErrorType.Validation, Exception? innerException = null)
        : base(message, innerException)
    {
        if (type == ErrorType.None)
            throw new ArgumentOutOfRangeException(nameof(type), type, "A user-facing exception cannot carry ErrorType.None.");
        Type = type;
    }

    /// <summary>Error category, from which the host derives its transport status.</summary>
    public ErrorType Type { get; }
}
