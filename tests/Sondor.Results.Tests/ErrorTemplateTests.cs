namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ErrorTemplateTests
{
    private static readonly string[] AccountAndLimit = ["Account", "Limit"];

    private static readonly ErrorTemplate Overdrawn = new(
        "Account.Overdrawn", ErrorType.Conflict, "The {Account} cannot go below the {Limit}.");

    [Test]
    public void Create_Should_Fill_The_Placeholders_And_Keep_The_Code_And_Type()
    {
        var error = Overdrawn.Create(ErrorArgument.Term("Account", "current account"), ErrorArgument.Term("Limit", "overdraft limit"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Code, Is.EqualTo("Account.Overdrawn"));
            Assert.That(error.Type, Is.EqualTo(ErrorType.Conflict));
            Assert.That(error.Description, Is.EqualTo("The current account cannot go below the overdraft limit."));
        }
    }

    [Test]
    public void Create_Should_Fill_The_Placeholders_Whatever_Order_The_Arguments_Come_In()
    {
        var error = Overdrawn.Create(ErrorArgument.Term("Limit", "overdraft limit"), ErrorArgument.Term("Account", "current account"));

        Assert.That(error.Description, Is.EqualTo("The current account cannot go below the overdraft limit."));
    }

    [Test]
    public void Create_Should_Keep_The_Arguments_And_Name_The_Template()
    {
        var error = Overdrawn.Create(ErrorArgument.Term("Account", "current account"), ErrorArgument.Term("Limit", "overdraft limit"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Arguments, Has.Count.EqualTo(2));
            Assert.That(error.Arguments.Template, Is.EqualTo("Account.Overdrawn"));
            Assert.That(error.Arguments.TryGetValue("Account", out var account), Is.True);
            Assert.That(account.Value, Is.EqualTo("current account"));
            Assert.That(account.Kind, Is.EqualTo(ErrorArgumentKind.Term));
            Assert.That(error.Arguments.TryGetValue("Missing", out _), Is.False);
        }
    }

    [Test]
    public void A_Template_Without_Placeholders_Should_Create_Its_Sentence_As_It_Is()
    {
        var template = new ErrorTemplate("Export.Busy", ErrorType.Conflict, "An export is already running.");

        var error = template.Create();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Description, Is.EqualTo("An export is already running."));
            Assert.That(error.Arguments, Is.Empty);
            Assert.That(error.Arguments.Template, Is.EqualTo("Export.Busy"));
        }
    }

    [Test]
    public void Doubled_Braces_Should_Be_Literal_Braces()
    {
        var template = new ErrorTemplate("Format.Bad", ErrorType.Validation, "Use {{braces}} around {Name}, like {{{Example}}}.");

        var error = template.Create(ErrorArgument.Term("Name", "this"), ErrorArgument.Term("Example", "that"));

        Assert.That(error.Description, Is.EqualTo("Use {braces} around this, like {that}."));
    }

    [Test]
    public void Placeholders_Should_List_The_Names_In_Order()
    {
        Assert.That(Overdrawn.Placeholders, Is.EqualTo(AccountAndLimit));
    }

    [TestCase("{0}", TestName = "Positional_Placeholder")]
    [TestCase("Value {Name:N2}", TestName = "Format_Specifier")]
    [TestCase("Value {Name,5}", TestName = "Alignment")]
    [TestCase("Empty {}", TestName = "Empty_Placeholder")]
    [TestCase("Spaced { Name }", TestName = "Spaces_In_The_Name")]
    [TestCase("Unclosed {Name", TestName = "Unclosed_Brace")]
    [TestCase("Unopened Name}", TestName = "Unopened_Brace")]
    [TestCase("Twice {Name} and {Name}", TestName = "Repeated_Placeholder")]
    public void A_Format_That_Is_Not_Named_Placeholders_Only_Should_Be_Rejected(string format)
    {
        Assert.That(() => new ErrorTemplate("Format.Bad", ErrorType.Failure, format), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void A_Blank_Code_Or_A_Null_Format_Should_Be_Rejected()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => new ErrorTemplate(" ", ErrorType.Failure, "x"), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => new ErrorTemplate("A.B", ErrorType.Failure, null!), Throws.InstanceOf<ArgumentNullException>());
        }
    }

    [Test]
    public void A_Placeholder_Without_An_Argument_Should_Be_Rejected()
    {
        Assert.That(() => Overdrawn.Create(ErrorArgument.Term("Account", "current account")), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void A_Term_That_Is_Not_A_Placeholder_Should_Be_Rejected()
    {
        Assert.That(
            () => Overdrawn.Create(ErrorArgument.Term("Account", "a"), ErrorArgument.Term("Limit", "b"), ErrorArgument.Term("Extra", "c")),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void The_Same_Argument_Twice_Should_Be_Rejected()
    {
        Assert.That(
            () => Overdrawn.Create(ErrorArgument.Term("Account", "a"), ErrorArgument.Term("Account", "b"), ErrorArgument.Term("Limit", "c")),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void A_Default_Argument_Should_Be_Rejected()
    {
        Assert.That(() => Overdrawn.Create(default, ErrorArgument.Term("Limit", "b")), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Extra_Data_And_Sensitive_Arguments_Should_Travel_With_The_Error_Without_Showing_In_The_Description()
    {
        var error = Overdrawn.Create(
            ErrorArgument.Term("Account", "current account"),
            ErrorArgument.Term("Limit", "overdraft limit"),
            ErrorArgument.Data("Balance", 12.5m),
            ErrorArgument.Sensitive("AccountNumber"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Description, Is.EqualTo("The current account cannot go below the overdraft limit."));
            Assert.That(error.Arguments, Has.Count.EqualTo(4));
            Assert.That(error.Arguments.TryGetValue("Balance", out var balance), Is.True);
            Assert.That(balance.Value, Is.EqualTo(12.5m));
        }
    }

    [Test]
    public void Data_Should_Not_Fill_A_Placeholder_Of_A_Template_That_Does_Not_Show_Data()
    {
        Assert.That(
            () => Overdrawn.Create(ErrorArgument.Data("Account", "12345"), ErrorArgument.Term("Limit", "b")),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Data_Should_Fill_A_Placeholder_Of_A_Template_Declared_To_Show_Data()
    {
        var template = new ErrorTemplate("Export.Missing", ErrorType.NotFound, "No file named {Name} was found.", showData: true);

        var error = template.Create(ErrorArgument.Data("Name", "report.csv"));

        Assert.That(error.Description, Is.EqualTo("No file named report.csv was found."));
    }

    [Test]
    public void Shown_Data_Should_Be_One_Short_Line()
    {
        var template = new ErrorTemplate("Export.Missing", ErrorType.NotFound, "No file named {Name} was found.", showData: true);

        var withBreaks = template.Create(ErrorArgument.Data("Name", "a\r\nb\tc"));
        var tooLong = template.Create(ErrorArgument.Data("Name", new string('x', 500)));
        var nothing = template.Create(ErrorArgument.Data("Name", null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withBreaks.Description, Is.EqualTo("No file named a  b c was found."));
            Assert.That(tooLong.Description, Is.EqualTo($"No file named {new string('x', 64)}… was found."));
            Assert.That(nothing.Description, Is.EqualTo("No file named  was found."));
        }
    }

    [Test]
    public void Shown_Data_Should_Not_Depend_On_The_Current_Culture()
    {
        var template = new ErrorTemplate("Export.Limit", ErrorType.Validation, "The limit {Limit} is too high.", showData: true);
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.That(template.Create(ErrorArgument.Data("Limit", 1234.5m)).Description, Is.EqualTo("The limit 1234.5 is too high."));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Test]
    public void A_Sensitive_Argument_Should_Show_As_Redacted_And_Keep_No_Value()
    {
        var template = new ErrorTemplate("Login.Failed", ErrorType.Unauthorized, "The sign-in for {User} failed.");

        var error = template.Create(ErrorArgument.Sensitive("User"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Description, Is.EqualTo("The sign-in for [redacted] failed."));
            Assert.That(error.Arguments.TryGetValue("User", out var user), Is.True);
            Assert.That(user.IsSensitive, Is.True);
            Assert.That(user.Value, Is.Null);
        }
    }

    [Test]
    public void WithCode_Should_Keep_The_Words_And_Type_Under_A_New_Code()
    {
        var specific = ErrorTemplates.NotFound.WithCode("Order.NotFound");

        var error = specific.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(specific.Code, Is.EqualTo("Order.NotFound"));
            Assert.That(specific.Type, Is.EqualTo(ErrorType.NotFound));
            Assert.That(specific.Format, Is.EqualTo(ErrorTemplates.NotFound.Format));
            Assert.That(specific.Placeholders, Is.EqualTo(ErrorTemplates.NotFound.Placeholders));
            Assert.That(error.Code, Is.EqualTo("Order.NotFound"));
            Assert.That(error.Description, Is.EqualTo("No Order was found for the given Id."));
            Assert.That(error.Arguments.Template, Is.EqualTo("Resource.NotFound"), "the general template stays reachable for a fallback");
        }
    }

    [Test]
    public void WithCode_Of_A_Derived_Template_Should_Still_Name_The_Original_Template()
    {
        var error = ErrorTemplates.NotFound.WithCode("Order.NotFound").WithCode("Order.Missing")
            .Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Code, Is.EqualTo("Order.Missing"));
            Assert.That(error.Arguments.Template, Is.EqualTo("Resource.NotFound"));
        }
    }

    [Test]
    public void Errors_Made_Separately_From_The_Same_Template_Should_Be_Equal()
    {
        var first = Overdrawn.Create(ErrorArgument.Term("Account", "a"), ErrorArgument.Term("Limit", "b"));
        var second = Overdrawn.Create(ErrorArgument.Term("Account", "a"), ErrorArgument.Term("Limit", "b"));
        var other = Overdrawn.Create(ErrorArgument.Term("Account", "a"), ErrorArgument.Term("Limit", "c"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(other));
        }
    }

    [Test]
    public void One_Template_Should_Be_Safe_To_Use_From_Many_Threads()
    {
        var failures = 0;

        Parallel.For(0, 20_000, index =>
        {
            var account = $"account {index}";
            var error = Overdrawn.Create(ErrorArgument.Term("Account", account), ErrorArgument.Term("Limit", "limit"));

            if (error.Description != $"The {account} cannot go below the limit.")
            {
                Interlocked.Increment(ref failures);
            }
        });

        Assert.That(failures, Is.Zero);
    }

    [Test]
    public void ToString_Should_Be_The_Code()
    {
        Assert.That(Overdrawn.ToString(), Is.EqualTo("Account.Overdrawn"));
    }
}
