using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stellaegis.Primitives;

/// <summary>
/// Who performs an operation, as recorded for traceability — a user identifier, a client identifier,
/// or the name a scheduled task gives itself. It does not tell users, machines and tasks apart: a
/// consumer that wants its records to say so prefixes the value, for example <c>job:purge</c>.
/// </summary>
/// <remarks>
/// The value comes from a token whose format is not ours, so no grammar applies: 1 to 128 characters,
/// no control character, no leading or trailing whitespace. The length counts UTF-16 code units, which
/// is never less than a database's count of characters — an actor accepted here fits a column limited
/// to 128 characters.
/// </remarks>
[JsonConverter(typeof(ActorJsonConverter))]
public sealed class Actor : IEquatable<Actor>, IParsable<Actor>
{
    /// <summary>The maximum length of an actor, in UTF-16 code units.</summary>
    public const int MaxLength = 128;

    private Actor(string value) => Value = value;

    /// <summary>The actor identifier.</summary>
    public string Value { get; }

    /// <summary>Reads an actor the code knows to be valid, such as the name of a scheduled task.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> does not follow the actor rule.</exception>
    public static Actor Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParse(s, out var actor)
            ? actor
            : throw new FormatException($"'{s}' is not a valid actor: 1 to {MaxLength} characters, no control character, no leading or trailing whitespace.");
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static Actor Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>Reads an actor from an input that may be invalid, such as a token claim.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Actor result)
    {
        result = IsValid(s) ? new Actor(s) : null;
        return result is not null;
    }

    /// <inheritdoc cref="TryParse(string?, out Actor)"/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Actor result)
        => TryParse(s, out result);

    /// <inheritdoc/>
    public bool Equals(Actor? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Actor a && Equals(a);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Actor? left, Actor? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Actor? left, Actor? right) => !(left == right);

    /// <summary>The actor identifier.</summary>
    public override string ToString() => Value;

    private static bool IsValid([NotNullWhen(true)] string? value)
        => value is { Length: > 0 and <= MaxLength }
           && !char.IsWhiteSpace(value[0])
           && !char.IsWhiteSpace(value[^1])
           && !value.Any(char.IsControl);
}

/// <summary>
/// Reads and writes an <see cref="Actor"/> as a JSON string, including as a dictionary key. Applied by the attribute on
/// the type: there is no need to register it.
/// </summary>
/// <remarks>
/// Public because source-generated serialization instantiates the converter from the consumer's
/// assembly, which cannot reach an internal type.
/// </remarks>
public sealed class ActorJsonConverter : JsonConverter<Actor>
{
    /// <inheritdoc/>
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"An actor is a JSON string, not {reader.TokenType}.");
        return ReadValue(reader.GetString());
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    /// <inheritdoc/>
    public override Actor ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadValue(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);

    private static Actor ReadValue(string? value)
        => Actor.TryParse(value, out var actor)
            ? actor
            : throw new JsonException($"'{value}' is not a valid actor.");
}
