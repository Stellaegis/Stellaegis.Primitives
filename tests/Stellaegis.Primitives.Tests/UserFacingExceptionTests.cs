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

#pragma warning disable CS0618 // the former constructor, kept for 4.1 code
    [Fact]
    public void The_former_constructor_carries_no_code()
    {
        var ex = new UserFacingException("Document not found.", ErrorType.NotFound);

        Assert.Equal(new Error(string.Empty, "Document not found.", ErrorType.NotFound), ex.Error);
        Assert.Equal("Document not found.", ex.Message);
    }

    [Fact]
    public void The_former_constructor_rejects_type_none()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserFacingException("Message.", ErrorType.None));
    }
#pragma warning restore CS0618

    private sealed class DocumentMissingException()
        : UserFacingException(Error.NotFound("Document.NotFound", "Document not found."));
}
