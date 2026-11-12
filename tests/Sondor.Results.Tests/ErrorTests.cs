namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ErrorTests
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);

    [Test]
    public void None_Should_Have_No_Code()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Error.None.IsNone, Is.True);
            Assert.That(Error.None.Code, Is.Empty);
            Assert.That(Error.None.Description, Is.Empty);
        }
    }

    [Test]
    public void Default_Error_Should_Be_None_Even_Though_Its_Strings_Are_Null()
    {
        Assert.That(default(Error).IsNone, Is.True);
    }

    [Test]
    public void Error_With_A_Code_Should_Not_Be_None()
    {
        Assert.That(NotFound.IsNone, Is.False);
    }

    [Test]
    public void Errors_With_The_Same_Parts_Should_Be_Equal()
    {
        var same = new Error("Node.NotFound", "There is no such node.", ErrorType.NotFound);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(same, Is.EqualTo(NotFound));
            Assert.That(same == NotFound, Is.True);
            Assert.That(same.GetHashCode(), Is.EqualTo(NotFound.GetHashCode()));
        }
    }

    [Test]
    public void Errors_With_A_Different_Type_Should_Not_Be_Equal()
    {
        var other = NotFound with { Type = ErrorType.Conflict };

        Assert.That(other, Is.Not.EqualTo(NotFound));
    }

    [Test]
    public void NullValue_Should_Be_A_Failure_Error_With_A_Code()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Error.NullValue.IsNone, Is.False);
            Assert.That(Error.NullValue.Code, Is.EqualTo("Error.NullValue"));
            Assert.That(Error.NullValue.Type, Is.EqualTo(ErrorType.Failure));
        }
    }

    [Test]
    public void Unspecified_Should_Be_An_Internal_Error_With_A_Code()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Error.Unspecified.IsNone, Is.False);
            Assert.That(Error.Unspecified.Code, Is.EqualTo("Error.Unspecified"));
            Assert.That(Error.Unspecified.Type, Is.EqualTo(ErrorType.InternalServerError));
        }
    }

    [Test]
    public void Error_Should_Deconstruct_Into_Its_Parts()
    {
        var (code, description, type) = NotFound;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo("Node.NotFound"));
            Assert.That(description, Is.EqualTo("There is no such node."));
            Assert.That(type, Is.EqualTo(ErrorType.NotFound));
        }
    }

    [Test]
    public void ErrorType_Values_Should_Keep_Their_Numbers()
    {
        // Consumers may store or transmit the number; a reordering would silently change what it means.
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)ErrorType.Failure, Is.Zero);
            Assert.That((int)ErrorType.Validation, Is.EqualTo(1));
            Assert.That((int)ErrorType.NotFound, Is.EqualTo(2));
            Assert.That((int)ErrorType.Conflict, Is.EqualTo(3));
            Assert.That((int)ErrorType.Unauthorized, Is.EqualTo(4));
            Assert.That((int)ErrorType.Forbidden, Is.EqualTo(5));
            Assert.That((int)ErrorType.InternalServerError, Is.EqualTo(6));
            Assert.That((int)ErrorType.FeatureDisabled, Is.EqualTo(7));
            Assert.That((int)ErrorType.RateLimited, Is.EqualTo(8));
        }
    }
}
