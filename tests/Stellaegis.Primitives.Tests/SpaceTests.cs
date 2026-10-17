namespace Stellaegis.Primitives.Tests;

public class SpaceTests
{
    [Fact]
    public void The_space_of_a_compartment_carries_it()
    {
        var space = Space.Of(Compartment.Parse("acme"));

        Assert.False(space.IsShared);
        Assert.Equal(Compartment.Parse("acme"), space.Compartment);
    }

    [Fact]
    public void The_shared_space_has_no_compartment()
    {
        Assert.True(Space.Shared.IsShared);
        Assert.Null(Space.Shared.Compartment);
    }

    [Fact]
    public void Of_rejects_null()
        => Assert.Throws<ArgumentNullException>(() => Space.Of(null!));

    [Fact]
    public void A_compartment_converts_to_its_space()
    {
        Space space = Compartment.Parse("acme");

        Assert.Equal(Space.Of(Compartment.Parse("acme")), space);
    }

    [Fact]
    public void A_null_compartment_converts_to_null_never_to_the_shared_space()
    {
        Compartment? unresolved = null;

        Space? space = unresolved;

        Assert.Null(space);
    }

    [Fact]
    public void Spaces_of_the_same_compartment_are_equal()
    {
        var a = Space.Of(Compartment.Parse("acme"));
        var b = Space.Of(Compartment.Parse("acme"));

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a == Space.Of(Compartment.Parse("other")));
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void The_shared_space_equals_only_itself()
    {
        Assert.Equal(Space.Shared, Space.Shared);
        Assert.False(Space.Shared == Space.Of(Compartment.Parse("acme")));
        Assert.False(Space.Of(Compartment.Parse("acme")) == Space.Shared);
    }

    [Fact]
    public void A_segment_is_the_compartment_or_the_shared_segment()
    {
        Assert.Equal("acme", Space.Of(Compartment.Parse("acme")).ToSegment());
        Assert.Equal("_shared", Space.Shared.ToSegment());
        Assert.Equal(Space.SharedSegment, Space.Shared.ToSegment());
    }

    [Fact]
    public void No_compartment_takes_the_shared_segment()
        => Assert.False(Compartment.TryParse(Space.SharedSegment, out _));

    [Fact]
    public void ToString_shows_the_compartment_or_shared()
    {
        Assert.Equal("acme", $"{Space.Of(Compartment.Parse("acme"))}");
        Assert.Equal("(shared)", $"{Space.Shared}");
    }
}
