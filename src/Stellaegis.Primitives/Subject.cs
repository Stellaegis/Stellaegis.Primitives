using System.Diagnostics.CodeAnalysis;

namespace Stellaegis.Primitives;

/// <summary>
/// The person an operation acts upon — whose account is blocked, whose second factor is reset, who is deactivated —,
/// named by the <c>sub</c> of their tokens. The <see cref="Actor"/> performs the operation; the subject undergoes it.
/// A route that acts on a person loads the subject and asks the resource-based authorization about it, so that the
/// decision sees whom the gesture reaches, not only what it is:
/// <code>
/// var subject = Subject.Parse(id);
/// if (!await http.AuthorizeResourceAsync(subject))
///     return Results.NotFound();
/// </code>
/// The application answers with a handler on <c>OperationRequirement</c> and <see cref="Subject"/>: the module that
/// knows the person holds the gesture, the one that knows their rights decides it, and neither depends on the other.
/// </summary>
/// <remarks>
/// The value follows the rule of an actor — 1 to <see cref="Actor.MaxLength"/> characters, no control character, no
/// leading or trailing whitespace —, and a subject is never a piece of work: <see cref="Actor.JobPrefix"/> is refused,
/// since no gesture acts upon a job. Two subjects are equal when their values are.
/// </remarks>
public sealed class Subject : IEquatable<Subject>, IParsable<Subject>
{
    private Subject(string value) => Value = value;

    /// <summary>The <c>sub</c> of the person the operation acts upon.</summary>
    public string Value { get; }

    /// <summary>Reads a subject the code knows to be valid.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> does not follow the subject rule.</exception>
    public static Subject Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParse(s, out var subject)
            ? subject
            : throw new FormatException(
                $"'{s}' is not a valid subject: 1 to {Actor.MaxLength} characters, no control character, no leading or trailing whitespace, and never a job ({Actor.JobPrefix}…).");
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static Subject Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>Reads a subject from an input that may be invalid, such as a route value.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Subject result)
    {
        result = Actor.IsValid(s) && !s.StartsWith(Actor.JobPrefix, StringComparison.Ordinal) ? new Subject(s) : null;
        return result is not null;
    }

    /// <inheritdoc cref="TryParse(string?, out Subject)"/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Subject result)
        => TryParse(s, out result);

    /// <summary>
    /// Whether <paramref name="actor"/> is this person: the value of who answers for the operation, whoever performs
    /// it on their behalf — a person acting through a client is still that person.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="actor"/> is <see langword="null"/>.</exception>
    public bool Is(Actor actor) => string.Equals(Value, Guard.NotNull(actor).Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public bool Equals(Subject? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Subject s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Subject? left, Subject? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Subject? left, Subject? right) => !(left == right);

    /// <summary>The <c>sub</c> of the person, <see cref="Value"/>.</summary>
    public override string ToString() => Value;
}
