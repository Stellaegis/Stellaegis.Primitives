using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Stellaegis.Primitives;

/// <summary>
/// The rules of the names a module declares, written once for every package that reads them. An identifier — an
/// operation's target and name, a capability — follows the grammar of table names, which permission codes follow
/// too. A module name follows the grammar of an API code: letters and digits only, so that it can become a URL
/// segment, a configuration key or an OAuth scope unchanged.
/// </summary>
public static partial class NameRules
{
    /// <summary>The identifier rule, as a message names it.</summary>
    public const string IdentifierRule =
        "1 to 63 lowercase letters, digits and single underscores, starting with a letter and ending with a letter or a digit";

    /// <summary>The module name rule, as a message names it.</summary>
    public const string ModuleNameRule = "3 to 63 lowercase letters and digits, starting with a letter";

    /// <summary>Whether <paramref name="value"/> is an identifier, such as <c>fiscal_years</c>. A hyphen is refused, never converted.</summary>
    public static bool IsIdentifier([NotNullWhen(true)] string? value) => value is { Length: <= 63 } && Identifier().IsMatch(value);

    /// <summary>Whether <paramref name="value"/> is a module name, such as <c>documents</c>.</summary>
    public static bool IsModuleName([NotNullWhen(true)] string? value) => value is not null && ModuleName().IsMatch(value);

    /// <summary>The name of <typeparamref name="TModule"/>, checked against the module name rule.</summary>
    /// <exception cref="ArgumentException">The name is not a valid module name.</exception>
    public static string ModuleNameOf<TModule>() where TModule : IStellaegisModule
    {
        var name = TModule.Name;
        if (!IsModuleName(name))
        {
            throw new ArgumentException(
                $"'{name}', the name of {typeof(TModule).Name}, is not a valid module name: {ModuleNameRule}.",
                nameof(TModule));
        }
        return name;
    }

    // \z, not $: $ also matches before a final "\n".
    [GeneratedRegex(@"^[a-z](?:_?[a-z0-9])*\z", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex(@"^[a-z][a-z0-9]{2,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ModuleName();
}
