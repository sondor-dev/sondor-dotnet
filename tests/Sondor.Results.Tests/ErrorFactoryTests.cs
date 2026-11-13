namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ErrorFactoryTests
{
    private static readonly string[] EntityAndProperty = ["Entity", "Property"];
    private static readonly string[] ActionOnly = ["Action"];
    private static readonly string[] FeatureOnly = ["Feature"];

    // The codes and the placeholder names are a contract that translations and clients depend on, so they are pinned here.
    // The English wording is pinned too: changing it is allowed in a minor release, and this is where it is noticed.
    private static IEnumerable<TestCaseData> Factories()
    {
        yield return new TestCaseData(Error.NotFound("Order", "Id"), "Resource.NotFound", ErrorType.NotFound,
            "No Order was found for the given Id.", EntityAndProperty).SetName("NotFound");
        yield return new TestCaseData(Error.Conflict("Customer", "Email"), "Resource.Conflict", ErrorType.Conflict,
            "A Customer with that Email already exists.", EntityAndProperty).SetName("Conflict");
        yield return new TestCaseData(Error.Invalid("Invoice", "DueDate"), "Resource.Invalid", ErrorType.Validation,
            "The DueDate of the Invoice is not valid.", EntityAndProperty).SetName("Invalid");
        yield return new TestCaseData(Error.Unauthorized("view invoices"), "Request.Unauthorized", ErrorType.Unauthorized,
            "You need to sign in to view invoices.", ActionOnly).SetName("Unauthorized");
        yield return new TestCaseData(Error.Forbidden("cancel the order"), "Request.Forbidden", ErrorType.Forbidden,
            "You do not have permission to cancel the order.", ActionOnly).SetName("Forbidden");
        yield return new TestCaseData(Error.RateLimited("search customers"), "Request.RateLimited", ErrorType.RateLimited,
            "Too many requests were made to search customers. Try again later.", ActionOnly).SetName("RateLimited");
        yield return new TestCaseData(Error.FeatureDisabled("Invoice export"), "Feature.Disabled", ErrorType.FeatureDisabled,
            "Invoice export is not available.", FeatureOnly).SetName("FeatureDisabled");
        yield return new TestCaseData(Error.Internal("save the order"), "Error.Internal", ErrorType.InternalServerError,
            "An unexpected error occurred while trying to save the order.", ActionOnly).SetName("Internal");
        yield return new TestCaseData(Error.Failed("import the file"), "Error.Failed", ErrorType.Failure,
            "Could not import the file.", ActionOnly).SetName("Failed");
    }

    [TestCaseSource(nameof(Factories))]
    public void Each_Factory_Should_Build_Its_Template(Error error, string code, ErrorType type, string description, string[] terms)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Code, Is.EqualTo(code));
            Assert.That(error.Type, Is.EqualTo(type));
            Assert.That(error.Description, Is.EqualTo(description));
            Assert.That(error.Arguments.Select(argument => argument.Name), Is.EqualTo(terms));
            Assert.That(error.Arguments.All(argument => argument.Kind == ErrorArgumentKind.Term), Is.True);
            Assert.That(error.Arguments.Template, Is.EqualTo(code));
            Assert.That(error.IsNone, Is.False);
        }
    }

    [Test]
    public void The_Built_In_Templates_Should_Use_The_Documented_Placeholder_Names()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ErrorTemplates.NotFound.Placeholders, Is.EqualTo(EntityAndProperty));
            Assert.That(ErrorTemplates.Unauthorized.Placeholders, Is.EqualTo(ActionOnly));
            Assert.That(ErrorTemplates.FeatureDisabled.Placeholders, Is.EqualTo(FeatureOnly));
            Assert.That(ErrorArgumentNames.Value, Is.EqualTo("Value"));
        }
    }

    [Test]
    public void NotFound_Should_Keep_The_Looked_Up_Value_As_Data_And_Not_Show_It()
    {
        var error = Error.NotFound("Customer", "Email", "someone@example.com");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Description, Is.EqualTo("No Customer was found for the given Email."));
            Assert.That(error.Description, Does.Not.Contain("someone@example.com"));
            Assert.That(error.Arguments.TryGetValue(ErrorArgumentNames.Value, out var value), Is.True);
            Assert.That(value.Kind, Is.EqualTo(ErrorArgumentKind.Data));
            Assert.That(value.Value, Is.EqualTo("someone@example.com"));
        }
    }

    [Test]
    public void Conflict_Should_Keep_The_Clashing_Value_As_Data_And_Not_Show_It()
    {
        var error = Error.Conflict("Customer", "Email", "someone@example.com");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Description, Does.Not.Contain("someone@example.com"));
            Assert.That(error.Arguments.TryGetValue(ErrorArgumentNames.Value, out var value), Is.True);
            Assert.That(value.Kind, Is.EqualTo(ErrorArgumentKind.Data));
        }
    }

    [Test]
    public void A_Blank_Term_Should_Be_Rejected()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => Error.NotFound(" ", "Id"), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => Error.NotFound("Order", null!), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => Error.Internal(""), Throws.InstanceOf<ArgumentException>());
        }
    }

    [Test]
    public void An_Error_From_A_Factory_Should_Make_A_Failed_Result_Like_Any_Other()
    {
        Result<int> result = Error.NotFound("Order", "Id");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo("Resource.NotFound"));
            Assert.That(result.Error.Arguments.TryGetValue("Entity", out var entity), Is.True);
            Assert.That(entity.Value, Is.EqualTo("Order"));
        }
    }

    [Test]
    public void Errors_From_The_Same_Factory_Call_Should_Be_Equal()
    {
        var first = Error.NotFound("Order", "Id");
        var second = Error.NotFound("Order", "Id");

        Assert.That(first, Is.EqualTo(second));
    }
}
