using Microsoft.AspNetCore.Authorization;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>The nature of an <see cref="Operation"/>, declared by the module rather than deduced from the HTTP verb.</summary>
public enum OperationKind
{
    /// <summary>Reads data without changing it.</summary>
    Read,

    /// <summary>Creates or changes data — restoring an item from the trash included.</summary>
    Write,

    /// <summary>Removes data.</summary>
    Delete,
}

/// <summary>
/// An operation of a module, such as reading a document. It labels a route as metadata, where the consumer
/// attaches its policies (<c>RequireAuthorizationPerOperation</c>), and it is the requirement a module
/// checks on a loaded resource (<c>AuthorizeResourceAsync</c>): the route and the data speak the same
/// language.
/// </summary>
/// <remarks>
/// Immutable, so that an operation shared as a static field cannot be changed by any code holding it. Two
/// operations are equal when their module, name and kind are. Administration has no kind of its own: a
/// module maps its administration routes separately, and the consumer secures them as it maps them.
/// </remarks>
public sealed class Operation : IAuthorizationRequirement, IEquatable<Operation>
{
    /// <summary>Creates an operation of <paramref name="module"/>, such as <c>new("documents", "read", OperationKind.Read)</c>.</summary>
    public Operation(string module, string name, OperationKind kind)
    {
        Module = Guard.NotWhiteSpace(module);
        Name = Guard.NotWhiteSpace(name);
        Kind = Guard.Satisfies(kind, Enum.IsDefined, "Unknown operation kind.");
    }

    /// <summary>The module the operation belongs to, such as <c>documents</c>.</summary>
    public string Module { get; }

    /// <summary>The operation name within its module, such as <c>read</c>.</summary>
    public string Name { get; }

    /// <summary>The nature of the operation.</summary>
    public OperationKind Kind { get; }

    /// <inheritdoc/>
    public bool Equals(Operation? other)
        => other is not null
           && string.Equals(Module, other.Module, StringComparison.Ordinal)
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && Kind == other.Kind;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Operation o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Module, Name, Kind);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Operation? left, Operation? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Operation? left, Operation? right) => !(left == right);

    /// <summary>The operation as <c>module.name</c>, such as <c>documents.read</c>.</summary>
    public override string ToString() => $"{Module}.{Name}";
}
