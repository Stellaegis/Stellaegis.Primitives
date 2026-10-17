using System.Text.RegularExpressions;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The rule of the names this package reads from a module — a module name, an operation's target and name: 1 to
/// 63 lowercase letters, digits and single hyphens, starting with a letter and ending with a letter or a digit.
/// </summary>
internal static partial class Slug
{
    public const string Rule =
        "1 to 63 lowercase letters, digits and single hyphens, starting with a letter and ending with a letter or a digit";

    public static bool IsValid(string? value) => value is { Length: <= 63 } && Pattern().IsMatch(value);

    [GeneratedRegex("^[a-z](?:-?[a-z0-9])*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
