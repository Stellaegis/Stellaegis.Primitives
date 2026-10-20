using System.Reflection;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// Labels an action, or every action of a controller, with an <see cref="AspNetCore.Operation"/> the module
/// declares as a static member — what <c>.WithMetadata(operation)</c> does on a route:
/// <code>
/// public static class NotesOperations
/// {
///     public static readonly Operation Read = new("notes", "read", OperationKind.Read);
/// }
///
/// [HttpGet("{id}"), Operation(typeof(NotesOperations), nameof(NotesOperations.Read))]
/// public IResult Get(string id) => ...;
/// </code>
/// </summary>
/// <remarks>
/// An attribute takes constants only, so it names the operation rather than holding it: the operation is written
/// once, where the module declares it. The member is read when the endpoints are built, and a missing member, or one
/// that is not an <see cref="AspNetCore.Operation"/>, fails the startup check. An action's label prevails over its
/// controller's.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class OperationAttribute(Type declaringType, string memberName) : Attribute
{
    private readonly Lazy<Operation> _operation = new(() => Resolve(declaringType, memberName));

    /// <summary>The type declaring the operation.</summary>
    public Type DeclaringType { get; } = declaringType;

    /// <summary>The name of the static field or property holding the operation.</summary>
    public string MemberName { get; } = memberName;

    /// <summary>The operation the member holds.</summary>
    /// <exception cref="InvalidOperationException">The member does not exist, or holds no <see cref="AspNetCore.Operation"/>.</exception>
    public Operation Operation => _operation.Value;

    private static Operation Resolve(Type declaringType, string memberName)
    {
        const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var value = declaringType?.GetField(memberName ?? string.Empty, Static)?.GetValue(null)
            ?? declaringType?.GetProperty(memberName ?? string.Empty, Static)?.GetValue(null);
        return value as Operation ?? throw new InvalidOperationException(
            $"[Operation(typeof({declaringType?.Name}), \"{memberName}\")] names no static field or property holding an "
            + "Operation. Name the member where the module declares the operation, with nameof.");
    }
}
