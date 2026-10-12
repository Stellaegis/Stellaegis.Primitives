namespace Stellaegis.Primitives.Tests;

public class GuardTests
{
    [Fact]
    public void NotNull_returns_the_value()
    {
        var value = new object();

        Assert.Same(value, Guard.NotNull(value));
    }

    [Fact]
    public void NotNull_throws_with_the_caller_argument_name()
    {
        object? customer = null;

        var ex = Assert.Throws<ArgumentNullException>(() => Guard.NotNull(customer));

        Assert.Equal(nameof(customer), ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NotEmpty_rejects_null_or_empty_strings(string? name)
    {
        var ex = Assert.Throws<ArgumentException>(() => Guard.NotEmpty(name));

        Assert.Equal(nameof(name), ex.ParamName);
    }

    [Fact]
    public void NotEmpty_accepts_whitespace()
    {
        Assert.Equal(" ", Guard.NotEmpty(" "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NotWhiteSpace_rejects_null_empty_or_whitespace(string? email)
    {
        var ex = Assert.Throws<ArgumentException>(() => Guard.NotWhiteSpace(email));

        Assert.Equal(nameof(email), ex.ParamName);
    }

    [Fact]
    public void NotWhiteSpace_returns_the_value()
    {
        Assert.Equal("a", Guard.NotWhiteSpace("a"));
    }

    [Fact]
    public void NotEmpty_rejects_the_empty_guid()
    {
        var documentId = Guid.Empty;

        var ex = Assert.Throws<ArgumentException>(() => Guard.NotEmpty(documentId));

        Assert.Equal(nameof(documentId), ex.ParamName);
    }

    [Fact]
    public void NotEmpty_returns_a_non_empty_guid()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id, Guard.NotEmpty(id));
    }

    [Fact]
    public void Satisfies_returns_the_value_when_the_condition_holds()
    {
        Assert.Equal(3, Guard.Satisfies(3, a => a >= 0, "Age must be positive."));
    }

    [Fact]
    public void Satisfies_throws_with_the_message_when_the_condition_fails()
    {
        var age = -1;

        var ex = Assert.Throws<ArgumentException>(() => Guard.Satisfies(age, a => a >= 0, "Age must be positive."));

        Assert.Equal(nameof(age), ex.ParamName);
        Assert.StartsWith("Age must be positive.", ex.Message);
    }

    [Fact]
    public void Satisfies_rejects_a_null_predicate()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => Guard.Satisfies(1, null!, "Message."));

        Assert.Equal("predicate", ex.ParamName);
    }
}
