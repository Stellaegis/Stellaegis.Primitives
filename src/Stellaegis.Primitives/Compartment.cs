using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Stellaegis.Primitives;

/// <summary>
/// The compartment a piece of data belongs to: the access boundary a module partitions its data by —
/// a tenant, an organization, a user, or the single compartment of a simple project. The consumer
/// names its compartments; this type knows no special value.
/// </summary>
/// <remarks>
/// <para>
/// Data that belongs to no compartment lives in the shared space of a module that has one,
/// <see cref="Space.Shared"/>, and never in a compartment set apart for it: a name the rule accepts is a
/// name a real compartment could carry.
/// </para>
/// <para>
/// The value is an identifier of 3 to 63 characters: a lowercase letter first, then lowercase letters,
/// digits and single hyphens, ending with a letter or a digit. Uppercase is rejected, never converted —
/// converting belongs to the edge that reads the input — so two equal compartments always carry the
/// same string, and an ordinal comparison is enough in a database, a cache key or a blob path.
/// </para>
/// <para>
/// A consumer that compartments by technical identifier prefixes it, for example <c>u</c> followed by a
/// GUID in the <c>N</c> format. On the wire and in a database the value stays a plain string.
/// </para>
/// </remarks>
[JsonConverter(typeof(CompartmentJsonConverter))]
public sealed partial class Compartment : IEquatable<Compartment>, IParsable<Compartment>
{
    private Compartment(string value) => Value = value;

    /// <summary>The compartment identifier.</summary>
    public string Value { get; }

    /// <summary>Reads a compartment the code knows to be valid, such as a constant.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="s"/> does not follow the compartment rule.</exception>
    public static Compartment Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return TryParse(s, out var compartment)
            ? compartment
            : throw new FormatException($"'{s}' is not a valid compartment: 3 to 63 lowercase letters, digits and single hyphens, starting with a letter and ending with a letter or a digit.");
    }

    /// <inheritdoc cref="Parse(string)"/>
    public static Compartment Parse(string s, IFormatProvider? provider) => Parse(s);

    /// <summary>Reads a compartment from an input that may be invalid, such as a route value.</summary>
    public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out Compartment result)
    {
        result = s is not null && Rule().IsMatch(s) ? new Compartment(s) : null;
        return result is not null;
    }

    /// <inheritdoc cref="TryParse(string?, out Compartment)"/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Compartment result)
        => TryParse(s, out result);

    /// <inheritdoc/>
    public bool Equals(Compartment? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Compartment c && Equals(c);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Compartment? left, Compartment? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Compartment? left, Compartment? right) => !(left == right);

    /// <summary>The compartment identifier.</summary>
    public override string ToString() => Value;

    // \z, not $: $ also matches before a final "\n", which would let "main\n" through.
    [GeneratedRegex(@"^(?!.*--)[a-z][a-z0-9-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex Rule();
}

/// <summary>
/// Reads and writes a <see cref="Compartment"/> as a JSON string, including as a dictionary key. Applied by the attribute on
/// the type: there is no need to register it.
/// </summary>
/// <remarks>
/// Public because source-generated serialization instantiates the converter from the consumer's
/// assembly, which cannot reach an internal type.
/// </remarks>
public sealed class CompartmentJsonConverter : JsonConverter<Compartment>
{
    /// <inheritdoc/>
    public override Compartment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"A compartment is a JSON string, not {reader.TokenType}.");
        return ReadValue(reader.GetString());
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Compartment value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    /// <inheritdoc/>
    public override Compartment ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadValue(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Compartment value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);

    private static Compartment ReadValue(string? value)
        => Compartment.TryParse(value, out var compartment)
            ? compartment
            : throw new JsonException($"'{value}' is not a valid compartment.");
}
