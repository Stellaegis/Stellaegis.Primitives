namespace Stellaegis.Primitives;

/// <summary>
/// Exception that is part of a library's contract: it carries an <see cref="Primitives.Error"/>, whose code lets
/// the caller recognize it and whose message describes what the caller can correct.
/// </summary>
/// <remarks>
/// Use this exception when a <see cref="Result{TValue}"/> return is not practical and you need to
/// communicate a business error to the caller. Translating its <see cref="ErrorType"/> to a transport status — an
/// HTTP code, for instance — is the host's job. The status decides what leaves: the message of a 4xx describes the
/// caller's input and is sent, the message of a 5xx describes the server and stays in its log outside Development;
/// the code is always sent.
/// </remarks>
/// <example>
/// <code>throw new UserFacingException(Error.Validation("Invoice.Amount", "Invoice amount must be greater than zero."));</code>
/// </example>
public class UserFacingException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="UserFacingException"/> carrying <paramref name="error"/>, whose
    /// message becomes the exception's.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="error"/> has no code, or is of type <see cref="ErrorType.None"/>.</exception>
    public UserFacingException(Error error, Exception? innerException = null)
        : base(Checked(error).Message, innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="UserFacingException"/> with a message and no code.
    /// </summary>
    /// <param name="message">A message describing what the caller can correct.</param>
    /// <param name="type">Error category. Defaults to <see cref="ErrorType.Validation"/>; <see cref="ErrorType.None"/> is rejected.</param>
    /// <param name="innerException">The exception that caused this error, if any.</param>
    [Obsolete("Pass an Error, whose code lets the caller recognize this exception. This constructor will be removed in the next major version.")]
    public UserFacingException(string message, ErrorType type = ErrorType.Validation, Exception? innerException = null)
        : base(message, innerException)
    {
        if (type == ErrorType.None)
            throw new ArgumentOutOfRangeException(nameof(type), type, "A user-facing exception cannot carry ErrorType.None.");
        Error = new Error(string.Empty, message, type);
    }

    /// <summary>The error this exception carries: its code, its message and its category.</summary>
    public Error Error { get; }

    /// <summary>Error category, from which the host derives its transport status.</summary>
    public ErrorType Type => Error.Type;

    private static Error Checked(Error error)
    {
        Guard.NotNull(error);
        Guard.NotWhiteSpace(error.Code);
        Guard.Satisfies(error, e => e.Type != ErrorType.None, "A user-facing exception cannot carry ErrorType.None.");
        return error;
    }
}
