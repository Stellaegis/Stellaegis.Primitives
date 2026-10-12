namespace Stellaegis.Primitives.Tests;

public class ResultTests
{
    private static readonly Error SomeError = Error.Validation("Some.Code", "Some message.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_its_error()
    {
        var result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Typed_success_exposes_its_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(42, result.ValueOrDefault);
    }

    [Fact]
    public void Typed_failure_throws_on_value_access()
    {
        var result = Result.Failure<int>(SomeError);

        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Equal(default, result.ValueOrDefault);
    }

    [Fact]
    public void Typed_failure_is_a_result()
    {
        Result result = Result.Failure<string>(SomeError);

        Assert.Same(SomeError, result.Error);
    }

    [Fact]
    public void Failure_without_error_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Result.Failure(null!));
    }

    [Fact]
    public void Serialization_writes_the_value_and_skips_the_guarded_accessor()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Result.Success("text"));

        Assert.Contains("\"value\":\"text\"", json);
        Assert.DoesNotContain("\"Value\"", json);
    }
}
