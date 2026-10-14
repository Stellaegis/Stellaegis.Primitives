using System.Diagnostics.CodeAnalysis;

namespace Stellaegis.Primitives;

/// <summary>
/// Where a module acts: a <see cref="Primitives.Compartment"/>, or the shared space holding the data that belongs
/// to no compartment. The shared space is not a compartment, so its <see cref="Compartment"/> is absent.
/// </summary>
/// <remarks>
/// <para>
/// A module's core receives a <see cref="Space"/>, never <see langword="null"/>: the shared space is a value, not
/// the absence of a compartment. Where an absence occurs — a compartment a request could not resolve — it stays
/// an absence, and never turns into the shared space.
/// </para>
/// <para>
/// Not every module has a shared space; one that has decides how it stores it. This type has no text form and
/// no serialization for that reason.
/// </para>
/// </remarks>
public sealed class Space : IEquatable<Space>
{
    private Space(Compartment? compartment) => Compartment = compartment;

    /// <summary>The shared space, holding the data that belongs to no compartment.</summary>
    public static Space Shared { get; } = new(null);

    /// <summary>The compartment, or <see langword="null"/> exactly when this is the shared space.</summary>
    public Compartment? Compartment { get; }

    /// <summary>Whether this is the shared space.</summary>
    [MemberNotNullWhen(false, nameof(Compartment))]
    public bool IsShared => Compartment is null;

    /// <summary>The space of a compartment.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="compartment"/> is <see langword="null"/>.</exception>
    public static Space Of(Compartment compartment) => new(Guard.NotNull(compartment));

    /// <summary>
    /// The space of a compartment, so that a compartment is passed where a space is expected. A
    /// <see langword="null"/> compartment converts to <see langword="null"/>, never to <see cref="Shared"/>.
    /// </summary>
    [return: NotNullIfNotNull(nameof(compartment))]
    public static implicit operator Space?(Compartment? compartment) => compartment is null ? null : Of(compartment);

    /// <inheritdoc/>
    public bool Equals(Space? other) => other is not null && Compartment == other.Compartment;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Space s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() => Compartment?.GetHashCode() ?? 0;

    /// <summary>Value equality.</summary>
    public static bool operator ==(Space? left, Space? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(Space? left, Space? right) => !(left == right);

    /// <summary>The compartment identifier, or <c>(shared)</c>. For display only: it is not a storage format.</summary>
    public override string ToString() => Compartment?.Value ?? "(shared)";
}
