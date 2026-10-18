namespace Stellaegis.Primitives.Tests;

public class ApplicationFrameTests
{
    [Fact]
    public void An_application_without_compartments_declares_nothing_else()
    {
        var frame = ApplicationFrame.WithoutCompartments;

        Assert.Equal(CompartmentCount.None, frame.Compartments);
        Assert.Null(frame.FixedCompartment);
        Assert.False(frame.HasSharedSpace);
        Assert.False(frame.OwnerIsolation);
        Assert.Equal("no compartments", frame.ToString());
    }

    [Fact]
    public void A_single_compartment_carries_its_name()
    {
        var frame = ApplicationFrame.SingleCompartment("acme");

        Assert.Equal(CompartmentCount.Single, frame.Compartments);
        Assert.Equal(Compartment.Parse("acme"), frame.FixedCompartment);
        Assert.Equal(frame.FixedCompartment, ApplicationFrame.SingleCompartment(Compartment.Parse("acme")).FixedCompartment);
    }

    [Fact]
    public void A_single_compartment_follows_the_compartment_rule()
    {
        Assert.Throws<FormatException>(() => ApplicationFrame.SingleCompartment("Acme"));
        Assert.Throws<ArgumentNullException>(() => ApplicationFrame.SingleCompartment((string)null!));
        Assert.Throws<ArgumentNullException>(() => ApplicationFrame.SingleCompartment((Compartment)null!));
    }

    [Fact]
    public void Many_compartments_have_no_fixed_one()
    {
        var frame = ApplicationFrame.ManyCompartments();

        Assert.Equal(CompartmentCount.Many, frame.Compartments);
        Assert.Null(frame.FixedCompartment);
    }

    [Fact]
    public void The_shared_space_and_owner_isolation_are_absent_unless_declared()
    {
        var frame = ApplicationFrame.ManyCompartments();
        Assert.False(frame.HasSharedSpace);
        Assert.False(frame.OwnerIsolation);

        var declared = frame.WithSharedSpace().WithOwnerIsolation();
        Assert.True(declared.HasSharedSpace);
        Assert.True(declared.OwnerIsolation);
        Assert.Equal("many compartments, shared space, owner isolation", declared.ToString());
    }

    [Fact]
    public void A_frame_is_immutable()
    {
        var single = ApplicationFrame.SingleCompartment("acme");

        var isolated = single.WithOwnerIsolation();

        Assert.False(single.OwnerIsolation);
        Assert.True(isolated.OwnerIsolation);
        Assert.Equal(single.FixedCompartment, isolated.FixedCompartment);
        Assert.False(ApplicationFrame.WithoutCompartments.HasSharedSpace);
    }

    [Fact]
    public void Without_compartments_there_is_neither_a_shared_space_nor_an_owner_isolation()
    {
        var shared = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.WithoutCompartments.WithSharedSpace());
        var owner = Assert.Throws<InvalidOperationException>(() => ApplicationFrame.WithoutCompartments.WithOwnerIsolation());

        Assert.Contains("SingleCompartment", shared.Message);
        Assert.Contains("isolation by owner", owner.Message);
    }

    [Fact]
    public void The_anonymous_actor_defaults_to_anonymous_and_can_be_named()
    {
        var frame = ApplicationFrame.SingleCompartment("acme");
        Assert.Equal(Actor.Parse("anonymous"), frame.AnonymousActor);
        Assert.Equal("single compartment 'acme'", frame.ToString());

        var named = frame.WithAnonymousActor("guest");
        Assert.Equal(Actor.Parse("guest"), named.AnonymousActor);
        Assert.Equal("single compartment 'acme', anonymous actor 'guest'", named.ToString());

        Assert.Throws<FormatException>(() => frame.WithAnonymousActor(" guest"));
        Assert.Throws<ArgumentNullException>(() => frame.WithAnonymousActor((Actor)null!));
    }
}
