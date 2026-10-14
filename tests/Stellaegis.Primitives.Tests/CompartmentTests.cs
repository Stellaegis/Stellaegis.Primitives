using System.Text.Json;

namespace Stellaegis.Primitives.Tests;

public class CompartmentTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("abc")]
    [InlineData("a1b")]
    [InlineData("acme-corp")]
    [InlineData("a-b-c")]
    [InlineData("u3f2504e04f8911d39a0c0305e82c3301")]
    public void A_valid_identifier_is_accepted(string value)
    {
        Assert.True(Compartment.TryParse(value, out var compartment));
        Assert.Equal(value, compartment.Value);
        Assert.Equal(value, Compartment.Parse(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]                 // 2 characters
    [InlineData("1ab")]                // digit first
    [InlineData("-ab")]                // hyphen first
    [InlineData("ab-")]                // hyphen last
    [InlineData("a--b")]               // double hyphen
    [InlineData("Main")]               // uppercase is rejected, not converted
    [InlineData("acme_corp")]
    [InlineData("acme corp")]
    [InlineData("main\n")]             // $ would let it through
    [InlineData(" main")]
    [InlineData("café")]
    public void An_invalid_identifier_is_rejected(string value)
    {
        Assert.False(Compartment.TryParse(value, out var compartment));
        Assert.Null(compartment);
        Assert.Throws<FormatException>(() => Compartment.Parse(value));
    }

    [Fact]
    public void Length_is_bounded_to_63_characters()
    {
        var longest = "a" + new string('b', 62);

        Assert.True(Compartment.TryParse(longest, out _));
        Assert.False(Compartment.TryParse(longest + "c", out _));
    }

    [Fact]
    public void Null_is_rejected()
    {
        Assert.False(Compartment.TryParse(null, out _));
        Assert.Throws<ArgumentNullException>(() => Compartment.Parse(null!));
    }

    [Fact]
    public void Compartments_with_the_same_value_are_equal()
    {
        var a = Compartment.Parse("acme");
        var b = Compartment.Parse("acme");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a == Compartment.Parse("other"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(null));
    }

    [Fact]
    public void ToString_returns_the_value()
        => Assert.Equal("acme", $"{Compartment.Parse("acme")}");

    [Fact]
    public void Json_reads_and_writes_a_string()
    {
        var json = JsonSerializer.Serialize(Compartment.Parse("acme"));

        Assert.Equal("\"acme\"", json);
        Assert.Equal(Compartment.Parse("acme"), JsonSerializer.Deserialize<Compartment>(json));
    }

    [Fact]
    public void Json_null_reads_as_null()
        => Assert.Null(JsonSerializer.Deserialize<Compartment>("null"));

    [Theory]
    [InlineData("\"Acme\"")]
    [InlineData("42")]
    public void Json_rejects_an_invalid_value(string json)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Compartment>(json));

    [Fact]
    public void Json_reads_and_writes_a_dictionary_key()
    {
        var counts = new Dictionary<Compartment, int> { [Compartment.Parse("acme")] = 3 };

        var json = JsonSerializer.Serialize(counts);

        Assert.Equal("""{"acme":3}""", json);
        Assert.Equal(3, JsonSerializer.Deserialize<Dictionary<Compartment, int>>(json)![Compartment.Parse("acme")]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Dictionary<Compartment, int>>("""{"Acme":3}"""));
    }

    [Fact]
    public void Json_source_generation_uses_the_converter()
    {
        var envelope = new PrimitivesEnvelope(Compartment.Parse("acme"), Actor.Parse("job:purge"));

        var json = JsonSerializer.Serialize(envelope, PrimitivesJsonContext.Default.PrimitivesEnvelope);

        Assert.Equal("""{"Compartment":"acme","Actor":"job:purge"}""", json);
        Assert.Equal(envelope, JsonSerializer.Deserialize(json, PrimitivesJsonContext.Default.PrimitivesEnvelope));
    }
}
