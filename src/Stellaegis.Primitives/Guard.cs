using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Stellaegis.Primitives;

/// <summary>
/// Guard helpers for argument validation (developer errors, not user errors).
/// Parameter names are captured automatically via <see cref="CallerArgumentExpressionAttribute"/> —
/// no <c>nameof()</c> required at call sites.
/// </summary>
public static class Guard
{
    /// <summary>Throws <see cref="ArgumentNullException"/> if <paramref name="value"/> is null.</summary>
    /// <example><code>Guard.NotNull(user);</code></example>
    public static T NotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : class
    {
        if (value is null) throw new ArgumentNullException(paramName);
        return value;
    }

    /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="value"/> is null or empty.</summary>
    /// <example><code>Guard.NotEmpty(name);</code></example>
    public static string NotEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty.", paramName);
        return value;
    }

    /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="value"/> is null, empty or whitespace.</summary>
    /// <example><code>Guard.NotWhiteSpace(email);</code></example>
    public static string NotWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null, empty or whitespace.", paramName);
        return value;
    }

    /// <summary>Throws <see cref="ArgumentException"/> if <paramref name="value"/> is <see cref="Guid.Empty"/>.</summary>
    /// <example><code>Guard.NotEmpty(documentId);</code></example>
    public static Guid NotEmpty(
        Guid value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Guid cannot be empty.", paramName);
        return value;
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> if <paramref name="predicate"/> returns <see langword="false"/>.
    /// The predicate describes the <b>required</b> condition — reads as a human assertion.
    /// </summary>
    /// <example><code>Guard.Satisfies(age, a => a >= 0, "Age must be positive.");</code></example>
    public static T Satisfies<T>(
        T value,
        Func<T, bool> predicate,
        string message,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (!predicate(value)) throw new ArgumentException(message, paramName);
        return value;
    }
}
