namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ResultOfTTests
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);
    private static readonly Error TooShort = new("Name.TooShort", "The name is too short.", ErrorType.Validation);

    [Test]
    public void A_Value_Should_Convert_To_A_Success()
    {
        Result<string> result = "node-1";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsFailure, Is.False);
            Assert.That(result.Value, Is.EqualTo("node-1"));
            Assert.That(result.Error, Is.EqualTo(Error.None));
        }
    }

    [Test]
    public void A_Null_Value_Should_Convert_To_The_NullValue_Failure()
    {
        Result<string> result = (string)null!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(Error.NullValue));
        }
    }

    [Test]
    public void An_Error_Should_Convert_To_A_Failure()
    {
        Result<string> result = NotFound;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void An_Error_Without_A_Code_Should_Not_Convert_To_A_Failure()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Result<string> _ = Error.None;
        });
    }

    [Test]
    public void The_Value_Of_A_Failure_Should_Not_Be_Readable_And_The_Message_Names_The_Failure()
    {
        Result<string> result = NotFound;

        var thrown = Assert.Throws<InvalidOperationException>(() => _ = result.Value);

        Assert.That(thrown!.Message, Does.Contain("Node.NotFound"));
    }

    [Test]
    public void Default_Result_Should_Be_A_Failure_Not_A_Success()
    {
        var result = default(Result<string>);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.EqualTo(Error.Unspecified));
            Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        }
    }

    [Test]
    public void A_Success_Should_Convert_To_A_Plain_Success()
    {
        Result plain = Result.Success(5);

        Assert.That(plain.IsSuccess, Is.True);
    }

    [Test]
    public void A_Failure_Should_Convert_To_A_Plain_Failure_With_The_Same_Error()
    {
        Result plain = Result.Failure<int>(NotFound);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plain.IsFailure, Is.True);
            Assert.That(plain.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void TryGetValue_Should_Return_The_Value_On_Success()
    {
        var found = Result.Success("node-1").TryGetValue(out var value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo("node-1"));
        }
    }

    [Test]
    public void TryGetValue_Should_Return_False_And_The_Default_On_Failure()
    {
        var found = Result.Failure<string>(NotFound).TryGetValue(out var value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(value, Is.Null);
        }
    }

    [Test]
    public void Match_Should_Run_The_Branch_That_Applies()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Result.Success(2).Match(value => value * 10, _ => -1), Is.EqualTo(20));
            Assert.That(Result.Failure<int>(NotFound).Match(value => value * 10, _ => -1), Is.EqualTo(-1));
        }
    }

    [Test]
    public void Tap_Should_Run_With_The_Value_On_Success_And_Return_The_Same_Result()
    {
        var seen = 0;
        var result = Result.Success(9);

        var returned = result.Tap(value => seen = value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seen, Is.EqualTo(9));
            Assert.That(returned, Is.EqualTo(result));
        }
    }

    [Test]
    public void Tap_Should_Not_Run_On_Failure()
    {
        var ran = false;

        var returned = Result.Failure<int>(NotFound).Tap(_ => ran = true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(returned.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Map_Should_Turn_The_Value_On_Success()
    {
        var result = Result.Success(3).Map(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.That(result.Value, Is.EqualTo("3"));
    }

    [Test]
    public void Map_Should_Pass_The_Failure_On_Without_Running()
    {
        var ran = false;

        var result = Result.Failure<int>(NotFound).Map(value =>
        {
            ran = true;

            return value;
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Map_To_Null_Should_Fail_With_The_NullValue_Error_Rather_Than_Throw()
    {
        // The mapped value goes through the same conversion as any other value: null is a failure, not an exception.
        var result = Result.Success(3).Map<string>(_ => null!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(Error.NullValue));
        }
    }

    [Test]
    public void Bind_Should_Run_The_Next_Step_With_The_Value_On_Success()
    {
        var result = Result.Success(4).Bind(value => Result.Success(value + 1));

        Assert.That(result.Value, Is.EqualTo(5));
    }

    [Test]
    public void Bind_Should_Be_Able_To_Fail_The_Chain()
    {
        var result = Result.Success(4).Bind(_ => Result.Failure<int>(NotFound));

        Assert.That(result.Error, Is.EqualTo(NotFound));
    }

    [Test]
    public void Bind_Should_Skip_The_Next_Step_On_Failure()
    {
        var ran = false;

        var result = Result.Failure<int>(NotFound).Bind(value =>
        {
            ran = true;

            return Result.Success(value);
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Ensure_Should_Keep_A_Success_That_Satisfies_The_Predicate()
    {
        var result = Result.Success("long-enough").Ensure(value => value.Length > 3, TooShort);

        Assert.That(result.Value, Is.EqualTo("long-enough"));
    }

    [Test]
    public void Ensure_Should_Turn_A_Success_That_Does_Not_Into_The_Given_Failure()
    {
        var result = Result.Success("ab").Ensure(value => value.Length > 3, TooShort);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(TooShort));
        }
    }

    [Test]
    public void Ensure_Should_Not_Test_A_Failure()
    {
        var ran = false;

        var result = Result.Failure<string>(NotFound).Ensure(_ =>
        {
            ran = true;

            return false;
        }, TooShort);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Ensure_With_An_Error_Without_A_Code_Should_Throw_When_The_Predicate_Fails()
    {
        Assert.Throws<ArgumentException>(() => Result.Success(1).Ensure(_ => false, Error.None));
    }

    [Test]
    public void Delegates_Should_Not_Be_Null()
    {
        var success = Result.Success(1);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentNullException>(() => success.Match<int>(null!, _ => 0));
            Assert.Throws<ArgumentNullException>(() => success.Match(value => value, null!));
            Assert.Throws<ArgumentNullException>(() => success.Tap(null!));
            Assert.Throws<ArgumentNullException>(() => success.Map<int>(null!));
            Assert.Throws<ArgumentNullException>(() => success.Bind<int>(null!));
            Assert.Throws<ArgumentNullException>(() => success.Ensure(null!, TooShort));
        }
    }

    [Test]
    public void Successes_Should_Be_Equal_When_Their_Values_Are()
    {
        var first = Result.Success("a");
        var second = Result.Success("a");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first == second, Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first != Result.Success("b"), Is.True);
        }
    }

    [Test]
    public void Failures_Should_Be_Equal_When_Their_Errors_Are()
    {
        var first = Result.Failure<string>(NotFound);
        var second = Result.Failure<string>(NotFound);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first, Is.Not.EqualTo(Result.Failure<string>(TooShort)));
        }
    }

    [Test]
    public void A_Success_Should_Not_Equal_A_Failure_Or_Another_Type()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Result.Success("a"), Is.Not.EqualTo(Result.Failure<string>(NotFound)));
            Assert.That(Result.Success("a").Equals((object)"a"), Is.False);
            Assert.That(Result.Success("a").Equals((object?)null), Is.False);
        }
    }

    [Test]
    public void ToString_Should_Not_Show_The_Value_Or_The_Description()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Result.Success("secret-value").ToString(), Is.EqualTo("Success"));
            Assert.That(Result.Failure<string>(NotFound).ToString(), Is.EqualTo("Failure(Node.NotFound)"));
        }
    }
}
