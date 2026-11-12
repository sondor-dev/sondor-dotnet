namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ResultStateTests
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);

    private static (bool IsSuccess, Error Error) Read(IResultState state) => (state.IsSuccess, state.Error);

    [Test]
    public void A_Result_Should_Be_Readable_Without_Its_Type()
    {
        var (isSuccess, error) = Read(Result.Failure(NotFound));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isSuccess, Is.False);
            Assert.That(error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void A_Result_Of_T_Should_Be_Readable_Without_Its_Type()
    {
        var (isSuccess, error) = Read(Result.Success(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(isSuccess, Is.True);
            Assert.That(error, Is.EqualTo(Error.None));
        }
    }
}
