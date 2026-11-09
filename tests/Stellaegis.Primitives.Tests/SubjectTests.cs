namespace Stellaegis.Primitives.Tests;

public class SubjectTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData("jane.doe@example.com")]
    public void The_sub_of_a_person_is_accepted_as_is(string value)
    {
        Assert.True(Subject.TryParse(value, out var subject));
        Assert.Equal(value, subject.Value);
        Assert.Equal(value, Subject.Parse(value).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" jane")]
    [InlineData("jane ")]
    [InlineData("ja\0ne")]
    [InlineData("job:purge")]
    public void A_value_an_actor_refuses_is_refused_and_so_is_a_job(string value)
    {
        Assert.False(Subject.TryParse(value, out var subject));
        Assert.Null(subject);
        Assert.Throws<FormatException>(() => Subject.Parse(value));
    }

    [Fact]
    public void Length_is_bounded_as_an_actor_is()
    {
        Assert.True(Subject.TryParse(new string('a', Actor.MaxLength), out _));
        Assert.False(Subject.TryParse(new string('a', Actor.MaxLength + 1), out _));
        Assert.False(Subject.TryParse(null, out _));
    }

    [Fact]
    public void Two_subjects_are_equal_by_value()
    {
        Assert.Equal(Subject.Parse("jane"), Subject.Parse("jane"));
        Assert.True(Subject.Parse("jane") == Subject.Parse("jane"));
        Assert.True(Subject.Parse("jane") != Subject.Parse("Jane"));
        Assert.Equal(Subject.Parse("jane").GetHashCode(), Subject.Parse("jane").GetHashCode());
    }

    [Fact]
    public void A_subject_is_the_actor_of_the_same_person_whoever_acts_on_their_behalf()
    {
        var jane = Subject.Parse("jane");

        Assert.True(jane.Is(Actor.Parse("jane")));
        Assert.True(jane.Is(Actor.Parse("jane").Through(Actor.Parse("crm_client"))));
        Assert.False(jane.Is(Actor.Parse("john")));
        Assert.False(jane.Is(Actor.Parse("crm_client").Through(Actor.Parse("jane"))));   // the client answers, jane only performs it
        Assert.Throws<ArgumentNullException>(() => jane.Is(null!));
    }
}
