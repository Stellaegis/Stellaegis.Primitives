using System.Text.Json.Serialization;

namespace Stellaegis.Primitives;

/// <summary>
/// Represents the outcome of an operation that can fail without throwing exceptions.
/// </summary>
public class Result
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Error detail when <see cref="IsFailure"/> is true; null on success.</summary>
    public Error? Error { get; }

    /// <summary>Initializes a result. Enforces the invariant: success ↔ no error.</summary>
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("A successful result cannot contain an error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("A failed result must contain an error.");

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Creates a successful result.</summary>
    public static Result Success() => new(true, null);

    /// <summary>Creates a failed result.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful result with a value.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);

    /// <summary>Creates a failed result for a typed result.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// Represents the outcome of an operation that returns a value on success.
/// </summary>
/// <typeparam name="TValue">Value type returned on success.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>
    /// Gets the value. Throws <see cref="InvalidOperationException"/> if <see cref="Result.IsFailure"/>.
    /// Always check <see cref="Result.IsSuccess"/> before accessing.
    /// </summary>
    [JsonIgnore]
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value on a failed result.");

    /// <summary>
    /// Serialization-safe value: returns the actual value on success, <c>default</c> on failure.
    /// Use <see cref="Value"/> in code (it guards against misuse).
    /// </summary>
    [JsonPropertyName("value")]
    public TValue? ValueOrDefault => _value;

    internal Result(TValue? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }
}
