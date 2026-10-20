namespace Stellaegis.Primitives.Tests;

public class KeySlotsTests
{
    [Fact]
    public void The_active_slot_gives_its_key_and_the_other_may_be_empty()
    {
        var slots = new KeySlots { Keys = { [1] = "first" }, ActiveKeySlot = 1 };

        Assert.Null(slots.FindError());
        Assert.Equal("first", slots.ActiveKey);
        Assert.Equal("first", slots.KeyOf(1));
        Assert.Equal(string.Empty, slots.KeyOf(2));
    }

    [Fact]
    public void A_rotation_moves_the_active_slot_and_keeps_the_former_key()
    {
        var rotated = new KeySlots { Keys = { [1] = "first", [2] = "second" }, ActiveKeySlot = 2 };

        Assert.Equal("second", rotated.ActiveKey);
        Assert.Equal("first", rotated.KeyOf(1));
    }

    [Theory]
    [InlineData(0, "ActiveKeySlot is 0")]
    [InlineData(3, "ActiveKeySlot is 3")]
    public void The_active_slot_is_1_or_2(int active, string error)
    {
        var slots = new KeySlots { Keys = { [1] = "first" }, ActiveKeySlot = active };

        Assert.Contains(error, slots.FindError());
        Assert.Throws<InvalidOperationException>(() => slots.ActiveKey);
    }

    [Fact]
    public void The_active_slot_needs_a_key()
    {
        Assert.Contains("No key is configured for the active slot 2: set Keys:2", new KeySlots { Keys = { [1] = "first" }, ActiveKeySlot = 2 }.FindError());
        Assert.Contains("No key is configured for the active slot 1", new KeySlots { Keys = { [1] = "  " } }.FindError());
        Assert.Contains("No key is configured for the active slot 1", new KeySlots().FindError());
        Assert.Throws<InvalidOperationException>(() => new KeySlots().KeyOf(2));
    }

    [Fact]
    public void A_key_outside_the_slots_is_refused()
        => Assert.Contains("Keys:3 is not a slot", new KeySlots { Keys = { [1] = "first", [3] = "third" } }.FindError());
}
