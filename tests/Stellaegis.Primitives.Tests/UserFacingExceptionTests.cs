namespace Stellaegis.Primitives.Tests;

public class UserFacingExceptionTests
{
    [Fact]
    public void Carries_its_error()
    {
        var error = Error.Validation("Invoice.Amount", "Invoice amount must be greater than zero.");

        var ex = new UserFacingException(error);

        Assert.Same(error, ex.Error);
        Assert.Equal(ErrorType.Validation, ex.Type);
        Assert.Equal("Invoice amount must be greater than zero.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Carries_its_inner_exception()
    {
        var inner = new InvalidOperationException();

        var ex = new UserFacingException(Error.NotFound("Document.NotFound", "Document not found."), inner);

        Assert.Equal(ErrorType.NotFound, ex.Type);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void An_error_without_code_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new UserFacingException(Error.Validation(" ", "Message.")));
    }

    [Fact]
    public void Type_none_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new UserFacingException(new Error("Code", "Message.", ErrorType.None)));
    }

    [Fact]
    public void Can_be_derived()
    {
        var ex = new DocumentMissingException();

        Assert.Equal(ErrorType.NotFound, ex.Type);
        Assert.Equal("Document.NotFound", ex.Error.Code);
    }


    private sealed class DocumentMissingException()
        : UserFacingException(Error.NotFound("Document.NotFound", "Document not found."));
}
