using System.Text.RegularExpressions;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The rules of the names this package reads from a module. An operation's target and name follow the grammar
/// of table names, which permission codes follow too. A module name follows the grammar of an API code: letters
/// and digits only, so that it can become a URL segment, a configuration key or an OAuth scope unchanged.
/// </summary>
internal static partial class NameRules
{
    public const string OperationPartRule =
        "1 to 63 lowercase letters, digits and single underscores, starting with a letter and ending with a letter or a digit";

    public const string ModuleRule =
        "3 to 63 lowercase letters and digits, starting with a letter";

    public static bool IsValidOperationPart(string? value) => value is { Length: <= 63 } && OperationPart().IsMatch(value);

    public static bool IsValidModule(string? value) => value is not null && Module().IsMatch(value);

    // \z, not $: $ also matches before a final "\n".
    [GeneratedRegex(@"^[a-z](?:_?[a-z0-9])*\z", RegexOptions.CultureInvariant)]
    private static partial Regex OperationPart();

    [GeneratedRegex(@"^[a-z][a-z0-9]{2,62}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Module();
}
