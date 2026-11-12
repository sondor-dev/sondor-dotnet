namespace Sondor.Results.Tests;

[TestFixture]
public sealed class ResultTests
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);
    private static readonly Error Conflict = new("Node.Exists", "The node already exists.", ErrorType.Conflict);

    [Test]
    public void Success_Should_Succeed_With_No_Error()
    {
        var result = Result.Success();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.IsFailure, Is.False);
            Assert.That(result.Error, Is.EqualTo(Error.None));
        }
    }

    [Test]
    public void Failure_Should_Fail_With_Its_Error()
    {
        var result = Result.Failure(NotFound);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Failure_Without_An_Error_Should_Throw()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
            Assert.Throws<ArgumentException>(() => Result.Failure(default));
            Assert.Throws<ArgumentException>(() => Result.Failure<int>(Error.None));
        }
    }

    [Test]
    public void An_Error_Should_Convert_To_A_Failure()
    {
        Result result = NotFound;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Default_Result_Should_Be_A_Failure_Not_A_Success()
    {
        var result = default(Result);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(Error.Unspecified));
        }
    }

    [Test]
    public void Success_With_A_Value_Should_Carry_It()
    {
        var result = Result.Success(42);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
        }
    }

    [Test]
    public void Success_With_A_Default_Value_Type_Should_Still_Succeed()
    {
        var result = Result.Success(0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Zero);
        }
    }

    [Test]
    public void Success_With_Null_Should_Throw_Because_A_Success_Has_A_Value()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
    }

    [Test]
    public void Failure_Of_T_Should_Fail_With_Its_Error()
    {
        var result = Result.Failure<string>(Conflict);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.EqualTo(Conflict));
        }
    }

    [Test]
    public void Match_Should_Run_The_Success_Branch_On_Success()
    {
        var outcome = Result.Success().Match(() => "ok", error => error.Code);

        Assert.That(outcome, Is.EqualTo("ok"));
    }

    [Test]
    public void Match_Should_Run_The_Failure_Branch_On_Failure()
    {
        var outcome = Result.Failure(NotFound).Match(() => "ok", error => error.Code);

        Assert.That(outcome, Is.EqualTo("Node.NotFound"));
    }

    [Test]
    public void Tap_Should_Run_On_Success_And_Return_The_Same_Result()
    {
        var ran = false;
        var result = Result.Success();

        var returned = result.Tap(() => ran = true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.True);
            Assert.That(returned, Is.EqualTo(result));
        }
    }

    [Test]
    public void Tap_Should_Not_Run_On_Failure()
    {
        var ran = false;

        var returned = Result.Failure(NotFound).Tap(() => ran = true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(returned.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Bind_Should_Return_The_Next_Result_On_Success()
    {
        var result = Result.Success().Bind(() => Result.Failure(Conflict));

        Assert.That(result.Error, Is.EqualTo(Conflict));
    }

    [Test]
    public void Bind_Should_Skip_The_Next_Step_On_Failure()
    {
        var ran = false;

        var result = Result.Failure(NotFound).Bind(() =>
        {
            ran = true;

            return Result.Success();
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Bind_To_A_Value_Should_Return_The_Next_Result_On_Success()
    {
        var result = Result.Success().Bind(() => Result.Success("next"));

        Assert.That(result.Value, Is.EqualTo("next"));
    }

    [Test]
    public void Bind_To_A_Value_Should_Pass_The_Failure_On()
    {
        var ran = false;

        var result = Result.Failure(NotFound).Bind(() =>
        {
            ran = true;

            return Result.Success("next");
        });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ran, Is.False);
            Assert.That(result.Error, Is.EqualTo(NotFound));
        }
    }

    [Test]
    public void Map_Should_Produce_A_Value_On_Success()
    {
        var result = Result.Success().Map(() => 7);

        Assert.That(result.Value, Is.EqualTo(7));
    }

    [Test]
    public void Map_Should_Pass_The_Failure_On_Without_Running()
    {
        var ran = false;

        var result = Result.Failure(NotFound).Map(() =>
        {
            ran = true;

            return 7;
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
        var result = Result.Success().Map<string>(() => null!);

        Assert.That(result.Error, Is.EqualTo(Error.NullValue));
    }

    [Test]
    public void Delegates_Should_Not_Be_Null()
    {
        var success = Result.Success();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentNullException>(() => success.Match<int>(null!, _ => 0));
            Assert.Throws<ArgumentNullException>(() => success.Match(() => 0, null!));
            Assert.Throws<ArgumentNullException>(() => success.Tap(null!));
            Assert.Throws<ArgumentNullException>(() => success.Bind((Func<Result>)null!));
            Assert.Throws<ArgumentNullException>(() => success.Bind((Func<Result<int>>)null!));
            Assert.Throws<ArgumentNullException>(() => success.Map<int>(null!));
        }
    }

    [Test]
    public void Successes_Should_Be_Equal()
    {
        var first = Result.Success();
        var second = Result.Success();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first == second, Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
        }
    }

    [Test]
    public void Failures_Should_Be_Equal_When_Their_Errors_Are()
    {
        var first = Result.Failure(NotFound);
        var second = Result.Failure(NotFound);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()));
            Assert.That(first != Result.Failure(Conflict), Is.True);
        }
    }

    [Test]
    public void A_Success_Should_Not_Equal_A_Failure()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Result.Success(), Is.Not.EqualTo(Result.Failure(NotFound)));
            Assert.That(Result.Success().Equals((object)"success"), Is.False);
            Assert.That(Result.Success().Equals((object?)null), Is.False);
        }
    }

    [Test]
    public void ToString_Should_Show_The_Code_But_Not_The_Description()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Result.Success().ToString(), Is.EqualTo("Success"));
            Assert.That(Result.Failure(NotFound).ToString(), Is.EqualTo("Failure(Node.NotFound)"));
            Assert.That(Result.Failure(NotFound).ToString(), Does.Not.Contain("no such node"));
        }
    }
}
