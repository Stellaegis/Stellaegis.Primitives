using System.Text.Json;

namespace Stellaegis.Primitives.Tests;

public class ActorTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("3f2504e04f8911d39a0c0305e82c3301")]
    [InlineData("t_acme_billing")]
    [InlineData("job:purge")]
    [InlineData("Jane Doe")]
    [InlineData("jane.doe@example.com")]
    public void A_value_from_a_token_is_accepted_as_is(string value)
    {
        Assert.True(Actor.TryParse(value, out var actor));
        Assert.Equal(value, actor.Value);
        Assert.Equal(value, Actor.Parse(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" jane")]
    [InlineData("jane ")]
    [InlineData("jane\n")]
    [InlineData("ja\0ne")]
    [InlineData("ja\tne")]
    public void An_invalid_value_is_rejected(string value)
    {
        Assert.False(Actor.TryParse(value, out var actor));
        Assert.Null(actor);
        Assert.Throws<FormatException>(() => Actor.Parse(value));
    }

    [Fact]
    public void Length_is_bounded_to_128_code_units()
    {
        var longest = new string('a', Actor.MaxLength);

        Assert.True(Actor.TryParse(longest, out _));
        Assert.False(Actor.TryParse(longest + "a", out _));
    }

    [Fact]
    public void Null_is_rejected()
    {
        Assert.False(Actor.TryParse(null, out _));
        Assert.Throws<ArgumentNullException>(() => Actor.Parse(null!));
    }

    [Fact]
    public void Actors_compare_ordinally()
    {
        Assert.Equal(Actor.Parse("jane"), Actor.Parse("jane"));
        Assert.True(Actor.Parse("jane") == Actor.Parse("jane"));
        Assert.True(Actor.Parse("jane") != Actor.Parse("Jane"));
        Assert.Equal(Actor.Parse("jane").GetHashCode(), Actor.Parse("jane").GetHashCode());
    }

    [Fact]
    public void ToString_returns_the_value()
        => Assert.Equal("job:purge", $"{Actor.Parse("job:purge")}");

    [Fact]
    public void Json_reads_and_writes_a_string()
    {
        var json = JsonSerializer.Serialize(Actor.Parse("jane"));

        Assert.Equal("\"jane\"", json);
        Assert.Equal(Actor.Parse("jane"), JsonSerializer.Deserialize<Actor>(json));
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\" jane\"")]
    [InlineData("true")]
    public void Json_rejects_an_invalid_value(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Actor>(json));

    [Fact]
    public void Json_reads_and_writes_a_dictionary_key()
    {
        var json = JsonSerializer.Serialize(new Dictionary<Actor, int> { [Actor.Parse("jane")] = 1 });

        Assert.Equal("""{"jane":1}""", json);
        Assert.Equal(1, JsonSerializer.Deserialize<Dictionary<Actor, int>>(json)![Actor.Parse("jane")]);
    }
}
