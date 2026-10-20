namespace Stellaegis.Primitives;

/// <summary>
/// A secret that rotates by slots: two keys, numbered 1 and 2, one of them active. New writes use the active key
/// and record its slot; what was written under the other key stays readable as long as that key is configured.
/// A rotation fills the free slot, moves the active slot to it, and retires the former key once nothing uses it.
/// <code>
/// Stellaegis:Documents:PostgreSQL:ActiveKeySlot = 1
/// Stellaegis:Documents:PostgreSQL:Keys:1        = passphrase-for-slot-1
/// Stellaegis:Documents:PostgreSQL:Keys:2        = (empty until the first rotation)
/// </code>
/// </summary>
/// <remarks>
/// The shape of the passphrases of a pgcrypto column, of a password pepper, of the key of a vault: one type for
/// every secret of the family that rotates, bound from a configuration section with these names. Supply the keys
/// from a secret store or the environment, never from a committed file. <see cref="FindError"/> is written for an
/// options validator run at startup: an active slot without a key fails the host, not the first write.
/// </remarks>
public sealed class KeySlots
{
    /// <summary>The slots a key may occupy: 1 and 2.</summary>
    public const int SlotCount = 2;

    /// <summary>The keys by slot, 1 or 2. A slot may be empty as long as nothing was written under it.</summary>
    public Dictionary<int, string> Keys { get; init; } = [];

    /// <summary>The slot whose key is used for new writes, 1 or 2.</summary>
    public int ActiveKeySlot { get; init; } = 1;

    /// <summary>The key of the active slot.</summary>
    /// <exception cref="InvalidOperationException">The slots do not hold: <see cref="FindError"/> says why.</exception>
    public string ActiveKey => KeyOf(ActiveKeySlot);

    /// <summary>
    /// The key of <paramref name="slot"/>: the active slot always has one; another slot may be empty, and reads as
    /// an empty string, as long as nothing was written under it.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="slot"/> is the active slot and has no key, or the slots do not hold.</exception>
    public string KeyOf(int slot)
    {
        if (FindError() is { } error)
            throw new InvalidOperationException(error);
        return Keys.TryGetValue(slot, out var key) && !string.IsNullOrWhiteSpace(key) ? key : string.Empty;
    }

    /// <summary>
    /// What is wrong with the slots, or <see langword="null"/>: the active slot is 1 or 2, it has a key, and no key
    /// is given for another slot than 1 and 2. Written for an options validator.
    /// </summary>
    public string? FindError()
    {
        if (ActiveKeySlot is < 1 or > SlotCount)
            return $"ActiveKeySlot is {ActiveKeySlot}: it must be 1 or {SlotCount}.";
        foreach (var slot in Keys.Keys)
        {
            if (slot is < 1 or > SlotCount)
                return $"Keys:{slot} is not a slot: the slots are 1 and {SlotCount}.";
        }
        if (!Keys.TryGetValue(ActiveKeySlot, out var key) || string.IsNullOrWhiteSpace(key))
            return $"No key is configured for the active slot {ActiveKeySlot}: set Keys:{ActiveKeySlot}.";
        return null;
    }
}
