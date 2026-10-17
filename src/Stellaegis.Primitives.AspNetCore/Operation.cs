using Microsoft.AspNetCore.Authorization;

namespace Stellaegis.Primitives.AspNetCore;

/// <summary>
/// The nature of an <see cref="Operation"/>: what it does to the data, declared by the module rather than deduced
/// from the HTTP verb. A fact, not a right.
/// </summary>
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
/// What a route does: an operation on a type of target, such as validating invoices, and the nature of what it
/// does to the data. A module labels each of its routes with one, as metadata.
/// <code>
/// public static readonly Operation ValidateInvoices = new("invoices", "validate", OperationKind.Write);
///
/// group.MapPost("/invoices/{id}/validate", ...).WithMetadata(ValidateInvoices);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The operation describes; it grants nothing. Its identity, <c>target.name</c> — <c>invoices.validate</c> — is
/// the key an application grants: a policy per operation (<c>RequireAuthorizationPerOperation</c>), a permission
/// in a rights system. Its <see cref="Kind"/> is a fact about the data, read for audit or to refuse writes during
/// a maintenance as much as for security. The module the route belongs to is declared on its group
/// (<c>BelongsTo</c>), not here: one module may act on several targets.
/// </para>
/// <para>
/// It is also the requirement a module checks on a loaded resource (<c>AuthorizeResourceAsync</c>): the route and
/// the data speak the same language. Immutable, so that an operation shared as a static field cannot be changed by
/// any code holding it. Two operations are equal when their target, name and kind are. Administration has no kind
/// of its own: it is an audience, not a nature, and a module maps its administration routes separately.
/// </para>
/// </remarks>
public sealed class Operation : IAuthorizationRequirement, IEquatable<Operation>
{
    /// <summary>
    /// Creates an operation on <paramref name="target"/>, such as <c>new("invoices", "validate", OperationKind.Write)</c>.
    /// Target and name are 1 to 63 lowercase letters, digits and single hyphens, starting with a letter and ending
    /// with a letter or a digit.
    /// </summary>
    /// <exception cref="ArgumentException">The target or the name breaks the rule, or the kind is unknown.</exception>
    public Operation(string target, string name, OperationKind kind)
    {
        Target = Guard.Satisfies(target, Slug.IsValid, $"'{target}' is not a valid operation target: {Slug.Rule}.");
        Name = Guard.Satisfies(name, Slug.IsValid, $"'{name}' is not a valid operation name: {Slug.Rule}.");
        Kind = Guard.Satisfies(kind, Enum.IsDefined, "Unknown operation kind.");
    }

    /// <summary>The type of target the operation acts on, such as <c>invoices</c> — a type, never an instance.</summary>
    public string Target { get; }

    /// <summary>The operation on its target, such as <c>validate</c>.</summary>
    public string Name { get; }

    /// <summary>The nature of the operation: what it does to the data.</summary>
    public OperationKind Kind { get; }

    /// <inheritdoc/>
    public bool Equals(Operation? other)
        => other is not null
           && string.Equals(Target, other.Target, StringComparison.Ordinal)
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && Kind == other.Kind;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Operation o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Target, Name, Kind);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Operation? left, Operation? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Operation? left, Operation? right) => !(left == right);

    /// <summary>The identity of the operation, <c>target.name</c>, such as <c>invoices.validate</c>.</summary>
    public override string ToString() => $"{Target}.{Name}";
}
