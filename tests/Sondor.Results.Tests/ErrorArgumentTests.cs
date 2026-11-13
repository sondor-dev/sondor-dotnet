namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ErrorArgumentTests
{
    [Test]
    public void A_Sensitive_Argument_Should_Keep_Only_Its_Name()
    {
        var argument = ErrorArgument.Sensitive("Password");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(argument.Name, Is.EqualTo("Password"));
            Assert.That(argument.Value, Is.Null);
            Assert.That(argument.Kind, Is.EqualTo(ErrorArgumentKind.Sensitive));
            Assert.That(argument.IsSensitive, Is.True);
        }
    }

    [Test]
    public void ToString_Should_Show_The_Name_And_Kind_But_Never_The_Value()
    {
        var term = ErrorArgument.Term("Entity", "Order");
        var data = ErrorArgument.Data("Value", "secret-looking-text");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(term.ToString(), Is.EqualTo("Entity (Term)"));
            Assert.That(data.ToString(), Is.EqualTo("Value (Data)"));
            Assert.That(data.ToString(), Does.Not.Contain("secret-looking-text"));
        }
    }

    [Test]
    public void Arguments_Should_Be_Equal_When_Name_Kind_And_Value_Match()
    {
        var order = ErrorArgument.Term("Entity", "Order");
        var sameOrder = ErrorArgument.Term("Entity", "Order");
        var customer = ErrorArgument.Term("Entity", "Customer");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(order, Is.EqualTo(sameOrder));
            Assert.That(order == sameOrder, Is.True);
            Assert.That(order.GetHashCode(), Is.EqualTo(sameOrder.GetHashCode()));
            Assert.That(order != customer, Is.True);
            Assert.That(ErrorArgument.Term("Entity", "x"), Is.Not.EqualTo(ErrorArgument.Data("Entity", "x")));
        }
    }

    [Test]
    public void A_Blank_Name_Should_Be_Rejected()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => ErrorArgument.Term("", "x"), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => ErrorArgument.Data(" ", "x"), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => ErrorArgument.Sensitive(null!), Throws.InstanceOf<ArgumentException>());
        }
    }

    [Test]
    public void A_Term_Needs_A_Value()
    {
        Assert.That(() => ErrorArgument.Term("Entity", " "), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Arguments_Should_List_Names_Only_In_ToString()
    {
        var error = ErrorTemplates.NotFound.Create(
            ErrorArgument.Term("Entity", "Order"),
            ErrorArgument.Term("Property", "Id"),
            ErrorArgument.Data("Value", "12345"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error.Arguments.ToString(), Is.EqualTo("[Entity, Property, Value]"));
            Assert.That(error.ToString(), Does.Not.Contain("12345"));
            Assert.That(error.ToString(), Does.Contain("Arguments = [Entity, Property, Value]"));
        }
    }

    [Test]
    public void Argument_Collections_Should_Compare_By_Value()
    {
        var first = ErrorTemplates.NotFound.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id")).Arguments;
        var second = ErrorTemplates.NotFound.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Id")).Arguments;
        var different = ErrorTemplates.NotFound.Create(ErrorArgument.Term("Entity", "Order"), ErrorArgument.Term("Property", "Number")).Arguments;

        var firstName = first[0].Name;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Equals(second), Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first.Equals(different), Is.False);
            Assert.That(first.Equals((ErrorArguments?)null), Is.False);
            Assert.That(firstName, Is.EqualTo("Entity"));
        }
    }
}
