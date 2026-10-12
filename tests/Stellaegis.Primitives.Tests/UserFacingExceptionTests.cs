namespace Stellaegis.Primitives.Tests;

public class UserFacingExceptionTests
{
    [Fact]
    public void Default_type_is_validation()
    {
        var ex = new UserFacingException("Invoice amount must be greater than zero.");

        Assert.Equal(ErrorType.Validation, ex.Type);
        Assert.Equal("Invoice amount must be greater than zero.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Carries_its_type_and_inner_exception()
    {
        var inner = new InvalidOperationException();

        var ex = new UserFacingException("Document not found.", ErrorType.NotFound, inner);

        Assert.Equal(ErrorType.NotFound, ex.Type);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Type_none_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserFacingException("Message.", ErrorType.None));
    }

    [Fact]
    public void Can_be_derived()
    {
        var ex = new DocumentMissingException();

        Assert.Equal(ErrorType.NotFound, ex.Type);
    }

    private sealed class DocumentMissingException() : UserFacingException("Document not found.", ErrorType.NotFound);
}
