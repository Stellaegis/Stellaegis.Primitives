using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Stellaegis.Primitives;

/// <summary>The form of the values a <see cref="Preference"/> accepts.</summary>
public enum PreferenceKind
{
    /// <summary>One value among a declared list, such as <c>light</c>, <c>dark</c> or <c>system</c>.</summary>
    Choice,

    /// <summary>A color, <c>#rrggbb</c> in lowercase hexadecimal.</summary>
    Color,

    /// <summary>A whole number between a declared minimum and maximum.</summary>
    Number,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,
}

/// <summary>
/// A preference a person sets for their own comfort — a theme, an accent color, a zoom — that only the clients read:
/// what the preferences module keeps, merges and returns without understanding it. A module declares its own on its
/// <see cref="ModuleDeclaration"/>; an application declares its own in its host's configuration, read by the
/// preferences module into the same definition.
/// <code>
/// public static class DocumentsPreferences
/// {
///     public static readonly Preference View = Preference.Choice("view", ["list", "grid"], "list");
/// }
///
/// services.AddStellaegisModule(ModuleDeclaration.Compartmented&lt;DocumentsModule&gt;().WithPreferences(DocumentsPreferences.View));
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// A value travels and is stored as a string, in its canonical form only: <c>#3b82f6</c>, <c>120</c>, <c>true</c>.
/// Another spelling of the same value is refused, never converted — <c>#3B82F6</c>, <c>0120</c>, <c>True</c>.
/// </para>
/// <para>
/// The name is an identifier, <see cref="NameRules.IdentifierRule"/>, read within its module: the key of a module's
/// preference is <c>module.name</c> (<see cref="ModuleDeclaration.Preferences"/>), the key of an application's is its
/// name alone, so the two never meet. A preference may have no default: a client then applies its own — the default
/// theme, when nobody chose an accent color. Immutable; two preferences are equal when their name, kind, accepted
/// values and default are.
/// </para>
/// </remarks>
public sealed partial class Preference : IEquatable<Preference>
{
    private readonly ImmutableArray<string> _choices;

    private Preference(string name, PreferenceKind kind, ImmutableArray<string> choices, int? minimum, int? maximum, string? defaultValue)
    {
        Name = Guard.Satisfies(name, NameRules.IsIdentifier, $"'{name}' is not a valid preference name: {NameRules.IdentifierRule}.");
        Kind = kind;
        _choices = choices;
        Minimum = minimum;
        Maximum = maximum;
        if (defaultValue is not null && Validate(defaultValue).IsFailure)
            throw new ArgumentException($"'{defaultValue}' is not a valid default for {name}: {Form}.", nameof(defaultValue));
        Default = defaultValue;
    }

    /// <summary>
    /// A preference taking one of <paramref name="values"/>, each an identifier, such as
    /// <c>Choice("theme", ["light", "dark", "system"], "system")</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="values"/>, or one of them, is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The name or a value is not an identifier, there is no value, a value is listed twice, or the default is not
    /// one of the values.
    /// </exception>
    public static Preference Choice(string name, IEnumerable<string> values, string? defaultValue = null)
    {
        var choices = Guard.NotNull(values).Select(value => Guard.NotNull(value)).ToImmutableArray();
        if (choices.IsEmpty)
            throw new ArgumentException($"{name} declares no value: a choice offers at least one.", nameof(values));
        foreach (var value in choices)
            Guard.Satisfies(value, NameRules.IsIdentifier, $"'{value}' is not a valid value for {name}: {NameRules.IdentifierRule}.");
        if (choices.Distinct(StringComparer.Ordinal).Count() != choices.Length)
            throw new ArgumentException($"{name} lists a value twice.", nameof(values));
        return new(name, PreferenceKind.Choice, choices, null, null, defaultValue);
    }

    /// <summary>A preference taking a color, <c>#rrggbb</c> in lowercase, such as <c>Color("accent")</c>.</summary>
    /// <exception cref="ArgumentException">The name is not an identifier, or the default is not a color in that form.</exception>
    public static Preference Color(string name, string? defaultValue = null)
        => new(name, PreferenceKind.Color, [], null, null, defaultValue);

    /// <summary>
    /// A preference taking a whole number from <paramref name="minimum"/> to <paramref name="maximum"/>, such as
    /// <c>Number("zoom", 50, 200, 100)</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not an identifier, the minimum exceeds the maximum, or the default is out of range.</exception>
    public static Preference Number(string name, int minimum, int maximum, int? defaultValue = null)
    {
        if (minimum > maximum)
            throw new ArgumentException($"{name} goes from {minimum} to {maximum}: the minimum exceeds the maximum.", nameof(minimum));
        return new(name, PreferenceKind.Number, [], minimum, maximum, defaultValue?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>A preference taking <c>true</c> or <c>false</c>, such as <c>Boolean("compact", false)</c>.</summary>
    /// <exception cref="ArgumentException">The name is not an identifier.</exception>
    public static Preference Boolean(string name, bool? defaultValue = null)
        => new(name, PreferenceKind.Boolean, [], null, null, defaultValue switch { true => "true", false => "false", null => null });

    /// <summary>The preference name, such as <c>theme</c>, read within its module.</summary>
    public string Name { get; }

    /// <summary>The form of the values it accepts.</summary>
    public PreferenceKind Kind { get; }

    /// <summary>The values a <see cref="PreferenceKind.Choice"/> accepts, in their declared order; empty for any other kind.</summary>
    public IReadOnlyList<string> Choices => _choices;

    /// <summary>The smallest value a <see cref="PreferenceKind.Number"/> accepts; <see langword="null"/> for any other kind.</summary>
    public int? Minimum { get; }

    /// <summary>The largest value a <see cref="PreferenceKind.Number"/> accepts; <see langword="null"/> for any other kind.</summary>
    public int? Maximum { get; }

    /// <summary>The value applied when the person chose none, in its canonical form; <see langword="null"/> when a client applies its own.</summary>
    public string? Default { get; }

    /// <summary>
    /// Whether <paramref name="value"/> is accepted, in its canonical form. Fails with
    /// <see cref="InvalidValueCode"/>, a validation error naming the form expected.
    /// </summary>
    public Result Validate(string? value)
    {
        var accepted = value is not null && Kind switch
        {
            PreferenceKind.Choice => _choices.Contains(value, StringComparer.Ordinal),
            PreferenceKind.Color => ColorValue().IsMatch(value),
            PreferenceKind.Number => NumberValue().IsMatch(value)
                && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                && number >= Minimum && number <= Maximum,
            _ => value is "true" or "false",
        };
        return accepted
            ? Result.Success()
            : Result.Failure(Error.Validation(InvalidValueCode, $"'{value}' is not a valid value for {Name}: {Form}."));
    }

    /// <summary>The code of the error <see cref="Validate"/> returns.</summary>
    public const string InvalidValueCode = "Preference.InvalidValue";

    private string Form => Kind switch
    {
        PreferenceKind.Choice => $"one of {string.Join(", ", _choices)}",
        PreferenceKind.Color => "a color, #rrggbb in lowercase",
        PreferenceKind.Number => $"a whole number from {Minimum} to {Maximum}, without leading zero or plus sign",
        _ => "true or false",
    };

    /// <inheritdoc/>
    public bool Equals(Preference? other)
        => other is not null
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && Kind == other.Kind
           && _choices.SequenceEqual(other._choices, StringComparer.Ordinal)
           && Minimum == other.Minimum
           && Maximum == other.Maximum
           && string.Equals(Default, other.Default, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Preference p && Equals(p);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Name, Kind, Minimum, Maximum, Default);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Preference? left, Preference? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Preference? left, Preference? right) => !(left == right);

    /// <summary>The preference name.</summary>
    public override string ToString() => Name;

    // \z, not $: $ also matches before a final "\n".
    [GeneratedRegex(@"^#[0-9a-f]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColorValue();

    // The canonical form of a whole number: no plus sign, no leading zero, no "-0".
    [GeneratedRegex(@"^(?:0|-?[1-9][0-9]{0,9})\z", RegexOptions.CultureInvariant)]
    private static partial Regex NumberValue();
}
