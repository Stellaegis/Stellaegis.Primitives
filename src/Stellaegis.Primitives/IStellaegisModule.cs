namespace Stellaegis.Primitives;

/// <summary>
/// The identity of a module: a type the module declares once, which carries its name. Code that needs to know a
/// module takes the type, never a string, so a name is written in one place and two names cannot be swapped.
/// <code>
/// public sealed class DocumentsModule : IStellaegisModule
/// {
///     public static string Name => "documents";
/// }
/// </code>
/// </summary>
public interface IStellaegisModule
{
    /// <summary>
    /// The module name, such as <c>documents</c>: 3 to 63 lowercase letters and digits, starting with a letter.
    /// Neither a hyphen nor an underscore: the name may become, unchanged, the API code of a host serving only
    /// this module — an OAuth scope and audience —, a URL segment or a configuration key. Checked where the type
    /// is first used.
    /// </summary>
    static abstract string Name { get; }
}
