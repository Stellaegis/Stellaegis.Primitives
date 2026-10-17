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
    /// The module name, such as <c>documents</c>: 1 to 63 lowercase letters, digits and single hyphens, starting
    /// with a letter and ending with a letter or a digit. Checked where the type is first used.
    /// </summary>
    static abstract string Name { get; }
}
