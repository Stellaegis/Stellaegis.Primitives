namespace Stellaegis.Primitives;

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
/// What a module does: an operation on a type of target, such as validating invoices, and the nature of what it
/// does to the data. A module declares its operations once, on its <see cref="ModuleDeclaration"/>, and labels each
/// of its routes with one of them; an operation with no route is a piece of work done outside requests.
/// <code>
/// public static class InvoiceOperations
/// {
///     public static readonly Operation Validate = new("invoices", "validate", OperationKind.Write);
/// }
///
/// services.AddStellaegisModule(ModuleDeclaration.Compartmented&lt;InvoicingModule&gt;().WithOperations(InvoiceOperations.Validate));
/// group.MapPost("/invoices/{id}/validate", ...).WithMetadata(InvoiceOperations.Validate);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The operation describes; it grants nothing. Its identity, <c>target.name</c> — <c>invoices.validate</c> — is
/// the key an application grants: a policy per operation, a permission in a rights system. Its <see cref="Kind"/>
/// is a fact about the data, read for audit or to refuse writes during a maintenance as much as for security. Its
/// <see cref="RequiresSecondFactor"/> says the operation is sensitive in itself — a purge, a payment —, whoever is
/// granted it: the requirement belongs to the elementary operation, never to a role that groups it. The
/// module the operation belongs to is the one that declares it: one module may act on several targets.
/// </para>
/// <para>
/// The declarations of every module, read together, are the catalogue of what an application can do. Immutable, so
/// that an operation shared as a static field cannot be changed by any code holding it. Two operations are equal
/// when their target, name, kind and requirement of a second factor are. Administration has no kind of its own: it is an audience, not a nature,
/// and a module maps its administration routes separately.
/// </para>
/// </remarks>
public sealed class Operation : IEquatable<Operation>
{
    /// <summary>
    /// Creates an operation on <paramref name="target"/>, such as <c>new("invoices", "validate", OperationKind.Write)</c>.
    /// Target and name are 1 to 63 lowercase letters, digits and single underscores, starting with a letter and
    /// ending with a letter or a digit — the grammar of table names: <c>fiscal_years.close</c>. A hyphen is
    /// refused, never converted.
    /// </summary>
    /// <exception cref="ArgumentException">The target or the name breaks the rule, or the kind is unknown.</exception>
    public Operation(string target, string name, OperationKind kind)
        : this(target, name, kind, requiresSecondFactor: false)
    {
    }

    /// <summary>
    /// Creates an operation that may require a second factor of whoever exercises it, such as
    /// <c>new("payroll", "pay", OperationKind.Write, requiresSecondFactor: true)</c>; otherwise as
    /// <see cref="Operation(string, string, OperationKind)"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The target or the name breaks the rule, or the kind is unknown.</exception>
    public Operation(string target, string name, OperationKind kind, bool requiresSecondFactor)
    {
        Target = Guard.Satisfies(target, NameRules.IsIdentifier, $"'{target}' is not a valid operation target: {NameRules.IdentifierRule}.");
        Name = Guard.Satisfies(name, NameRules.IsIdentifier, $"'{name}' is not a valid operation name: {NameRules.IdentifierRule}.");
        Kind = Guard.Satisfies(kind, Enum.IsDefined, "Unknown operation kind.");
        RequiresSecondFactor = requiresSecondFactor;
    }

    /// <summary>The type of target the operation acts on, such as <c>invoices</c> — a type, never an instance.</summary>
    public string Target { get; }

    /// <summary>The operation on its target, such as <c>validate</c>.</summary>
    public string Name { get; }

    /// <summary>The nature of the operation: what it does to the data.</summary>
    public OperationKind Kind { get; }

    /// <summary>
    /// Whether exercising the operation requires a session whose person used a second factor — the <c>amr</c> of the
    /// session says so. A fact the module declares about an operation sensitive in itself: a purge, a deletion, a
    /// payment; rarely a read, never by default. A rights system may harden it, never weaken it.
    /// </summary>
    public bool RequiresSecondFactor { get; }

    /// <summary>The identity of the operation, <c>target.name</c>, such as <c>invoices.validate</c>: the key an application grants.</summary>
    public string Identity => $"{Target}.{Name}";

    /// <inheritdoc/>
    public bool Equals(Operation? other)
        => other is not null
           && string.Equals(Target, other.Target, StringComparison.Ordinal)
           && string.Equals(Name, other.Name, StringComparison.Ordinal)
           && Kind == other.Kind
           && RequiresSecondFactor == other.RequiresSecondFactor;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Operation o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Target, Name, Kind, RequiresSecondFactor);

    /// <summary>Value equality.</summary>
    public static bool operator ==(Operation? left, Operation? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Operation? left, Operation? right) => !(left == right);

    /// <summary>The identity of the operation, <c>target.name</c>, such as <c>invoices.validate</c>.</summary>
    public override string ToString() => Identity;
}
