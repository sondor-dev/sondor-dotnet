namespace Sondor.Cqrs.Tests;

[TestFixture]
public sealed class RequestFailureTests
{
    [Test]
    public void Errors_Should_BeNull_When_TheFailureIsMadeWithoutThem()
    {
        var failure = new RequestFailure("Validation.Failed", "Name is required.", FailureKind.Validation);

        Assert.That(failure.Errors, Is.Null);
    }

    [Test]
    public void Errors_Should_CarryTheMessagesOfEachProperty_When_TheyAreGiven()
    {
        var errors = new Dictionary<string, string[]> { ["Name"] = ["Name is required."] };

        var failure = new RequestFailure("Validation.Failed", "Name is required.", FailureKind.Validation) { Errors = errors };

        Assert.That(failure.Errors, Is.SameAs(errors));
    }

    [Test]
    public void Deconstruct_Should_StillGiveTheCodeTheDescriptionAndTheKind_When_ErrorsAreGiven()
    {
        var failure = new RequestFailure("Validation.Failed", "Name is required.", FailureKind.Validation)
        {
            Errors = new Dictionary<string, string[]> { ["Name"] = ["Name is required."] },
        };

        var (code, description, kind) = failure;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(code, Is.EqualTo("Validation.Failed"));
            Assert.That(description, Is.EqualTo("Name is required."));
            Assert.That(kind, Is.EqualTo(FailureKind.Validation));
        }
    }
}
