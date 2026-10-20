using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stellaegis.Primitives;

/// <summary>
/// Who answers for an operation, as recorded for traceability — a user identifier, a client identifier, or the
/// name a scheduled task gives itself — and, when someone else performs it on their behalf, who that is:
/// <see cref="Via"/>, the acting party of an OAuth token exchange, the worker running what a user asked for.
/// <code>
/// var jane = Actor.Parse("jane");                       // the sub of a token
/// var purge = Actor.Job("purge");                       // job:purge, a piece of work outside requests
/// var provisioning = jane.Through(Actor.Job("provision"));   // jane answers; the provisioning job acts for her
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The value comes from a token whose format is not ours, so no grammar applies: 1 to 128 characters, no control
/// character, no leading or trailing whitespace. It is a stable, opaque identifier — the <c>sub</c> of a token, never
/// an e-mail address or a name: modules copy it into every record they write, and deleting an account does not
/// remove it from there. The length counts UTF-16 code units, which is never less than a database's count of
/// characters — an actor accepted here fits a column limited to 128 characters.
/// </para>
/// <para>
/// The value does not tell users, machines and tasks apart; a prefix does, which the named factories write —
/// <c>job:purge</c>. The acting party is a mention, never a second actor: the records of a module carry the value,
/// and an adapter that wants the mention adds a column of its own. Two actors are equal when their values and their
/// acting parties are.
/// </para>
/// </remarks>
[JsonConverter(typeof(ActorJsonConverter))]
public sealed class Actor : IEquatable<Actor>, IParsable<Actor>
{
    /// <summary>The maximum length of an actor, in UTF-16 code units.</summary>
    public const int MaxLength = 128;

    /// <summary>The prefix of a piece of work outside requests, <c>job:</c>.</summary>
    public const string JobPrefix = "job:";

    private Actor(string value, Actor? via)
    {
        Value = value;
        Via = via;
    }

    /// <summary>The identifier of who answers for the operation.</summary>
    public string Value { get; }

    /// <summary>Who performs the operation on behalf of <see cref="Value"/>, or <see langword="null"/> when nobody does.</summary>
    public Actor? Via { get; }

    /// <summary>Reads an actor the code knows to be valid, such as the identifier of a client.</summary>
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
        result = IsValid(s) ? new Actor(s, via: null) : null;
        return result is not null;
    }

    /// <inheritdoc cref="TryParse(string?, out Actor)"/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Actor result)
        => TryParse(s, out result);

    /// <summary>
    /// The actor of a piece of work done outside requests, <c>job:</c> followed by <paramref name="name"/> — a
    /// scheduled purge, an import. The name is an identifier, <see cref="NameRules.IdentifierRule"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid identifier.</exception>
    public static Actor Job(string name)
        => new(JobPrefix + Guard.Satisfies(name, NameRules.IsIdentifier, $"'{name}' is not a valid job name: {NameRules.IdentifierRule}."), via: null);

    /// <summary>
    /// This actor, with <paramref name="via"/> performing the operation on its behalf: the client of a token exchange,
    /// the worker running what this actor asked for. Responsibility does not move: the value stays this actor's.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="via"/> is <see langword="null"/>.</exception>
    public Actor Through(Actor via) => new(Value, Guard.NotNull(via));

    /// <inheritdoc/>
    public bool Equals(Actor? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal) && Via == other.Via;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Actor a && Equals(a);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Value), Via);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Actor? left, Actor? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Actor? left, Actor? right) => !(left == right);

    /// <summary>The identifier of who answers for the operation, <see cref="Value"/>: what a record carries.</summary>
    public override string ToString() => Value;

    private static bool IsValid([NotNullWhen(true)] string? value)
        => value is { Length: > 0 and <= MaxLength }
           && !char.IsWhiteSpace(value[0])
           && !char.IsWhiteSpace(value[^1])
           && !value.Any(char.IsControl);
}

/// <summary>
/// Reads and writes an <see cref="Actor"/> as JSON: a string when nobody acts on its behalf, an object
/// <c>{ "value": …, "via": … }</c> otherwise, both read back; as a dictionary key, its value alone. Applied by the
/// attribute on the type: there is no need to register it.
/// </summary>
/// <remarks>
/// Public because source-generated serialization instantiates the converter from the consumer's
/// assembly, which cannot reach an internal type.
/// </remarks>
public sealed class ActorJsonConverter : JsonConverter<Actor>
{
    private const string ValueName = "value";
    private const string ViaName = "via";

    /// <inheritdoc/>
    public override Actor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return ReadValue(reader.GetString());
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"An actor is a JSON string or an object with a value, not {reader.TokenType}.");

        string? value = null;
        Actor? via = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            if (string.Equals(name, ValueName, StringComparison.OrdinalIgnoreCase))
                value = reader.TokenType == JsonTokenType.String ? reader.GetString() : throw new JsonException("The value of an actor is a JSON string.");
            else if (string.Equals(name, ViaName, StringComparison.OrdinalIgnoreCase))
                via = reader.TokenType == JsonTokenType.Null ? null : Read(ref reader, typeToConvert, options);
            else
                reader.Skip();
        }
        var actor = ReadValue(value);
        return via is null ? actor : actor.Through(via);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Actor value, JsonSerializerOptions options)
    {
        if (value.Via is null)
        {
            writer.WriteStringValue(value.Value);
            return;
        }
        writer.WriteStartObject();
        writer.WriteString(ValueName, value.Value);
        writer.WritePropertyName(ViaName);
        Write(writer, value.Via, options);
        writer.WriteEndObject();
    }

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
