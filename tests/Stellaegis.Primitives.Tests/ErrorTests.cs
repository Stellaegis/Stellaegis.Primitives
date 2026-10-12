namespace Stellaegis.Primitives.Tests;

public class ErrorTests
{
    [Fact]
    public void Errors_with_same_code_and_type_are_equal_whatever_the_message()
    {
        var a = Error.NotFound("Document.NotFound", "First wording.");
        var b = Error.NotFound("Document.NotFound", "Second wording.");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Errors_with_same_code_and_different_type_differ()
    {
        var a = Error.NotFound("Document.Missing", "Message.");
        var b = Error.Conflict("Document.Missing", "Message.");

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact]
    public void Null_comparisons_are_safe()
    {
        Error? none = null;

        Assert.True(none == null);
        Assert.False(Error.None == null);
        Assert.False(Error.None.Equals(null));
    }

    [Theory]
    [InlineData(ErrorType.Failure)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.PreconditionFailed)]
    public void Each_factory_produces_the_category_it_is_named_after(ErrorType type)
    {
        Func<string, string, Error> factory = type switch
        {
            ErrorType.Failure => Error.Failure,
            ErrorType.Validation => Error.Validation,
            ErrorType.NotFound => Error.NotFound,
            ErrorType.Conflict => Error.Conflict,
            ErrorType.Unauthorized => Error.Unauthorized,
            ErrorType.Forbidden => Error.Forbidden,
            ErrorType.PreconditionFailed => Error.PreconditionFailed,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var error = factory("Some.Code", "Some message.");

        Assert.Equal(type, error.Type);
        Assert.Equal("Some.Code", error.Code);
        Assert.Equal("Some message.", error.Message);
    }

    [Fact]
    public void Default_type_is_failure()
    {
        Assert.Equal(ErrorType.Failure, new Error("Some.Code", "Some message.").Type);
    }
}
