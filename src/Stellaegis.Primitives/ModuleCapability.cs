namespace Stellaegis.Primitives;

/// <summary>
/// Something a module can do that depends on its nature or on its storage — be compatible with an isolation by
/// owner, inherit the configuration of the shared space. The module declares the capabilities it has; the assembly
/// of an application chooses among them, and nothing outside them.
/// <code>
/// public static readonly ModuleCapability InheritSharedSpace = new("inherit_shared_space", requiresSharedSpace: true);
/// </code>
/// </summary>
/// <remarks>
/// A capability is a type, not a string, so that a module and the application that chooses it name the same
/// thing. Its name is an identifier, <see cref="NameRules.IdentifierRule"/>, and it is read within its module:
/// two modules may each declare a capability of the same name. Two capabilities are equal when their names are.
/// A capability that supposes a shared space says so: turned on as a parameter in an application whose frame
/// declares none, it fails the startup.
/// </remarks>
public sealed class ModuleCapability : IEquatable<ModuleCapability>
{
    /// <summary>
    /// The module is compatible with the isolation by owner an application declares, <see cref="ApplicationFrame.OwnerIsolation"/>:
    /// each actor sees only the data it owns. Required of every module with compartments by such an application.
    /// Declared by the adapter that holds it when the database isolates data an actor owns — a row-level security
    /// rule written for one engine —, and by the module's core when none of its data belongs to an actor — a
    /// messaging outbox, a vault: the isolation then holds by nature, and the module is compatible with it.
    /// </summary>
    public static ModuleCapability OwnerIsolation { get; } = new("owner_isolation");

    /// <summary>
    /// Creates a capability named <paramref name="name"/>, such as <c>inherit_shared_space</c>, which
    /// <paramref name="requiresSharedSpace"/> marks as supposing a shared space in the application.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid identifier.</exception>
    public ModuleCapability(string name, bool requiresSharedSpace = false)
    {
        Name = Guard.Satisfies(name, NameRules.IsIdentifier, $"'{name}' is not a valid capability name: {NameRules.IdentifierRule}.");
        RequiresSharedSpace = requiresSharedSpace;
    }

    /// <summary>The capability name, such as <c>owner_isolation</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Whether the capability supposes a shared space: inheriting the shared space's configuration has no object
    /// without one. Turned on in an application without a shared space, the parameter fails the startup.
    /// </summary>
    public bool RequiresSharedSpace { get; }

    /// <inheritdoc/>
    public bool Equals(ModuleCapability? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ModuleCapability c && Equals(c);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    /// <summary>Value equality.</summary>
    public static bool operator ==(ModuleCapability? left, ModuleCapability? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(ModuleCapability? left, ModuleCapability? right) => !(left == right);

    /// <summary>The capability name.</summary>
    public override string ToString() => Name;
}
