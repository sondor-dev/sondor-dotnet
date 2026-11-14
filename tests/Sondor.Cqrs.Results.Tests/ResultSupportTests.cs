using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Sondor.Results;

namespace Sondor.Cqrs.Results.Tests;

[TestFixture]
public sealed class ResultSupportTests
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);

    [Test]
    public void The_Outcome_Of_A_Success_Should_Be_A_Success()
    {
        using var provider = Build();

        var outcome = provider.GetRequiredService<IOutcomeReader<Result>>().Read(Result.Success());

        Assert.That(outcome, Is.EqualTo(RequestOutcome.Success));
    }

    [Test]
    public void The_Outcome_Of_A_Failure_Should_Be_A_Failure_With_The_Error_Code()
    {
        using var provider = Build();

        var plain = provider.GetRequiredService<IOutcomeReader<Result>>().Read(Result.Failure(NotFound));
        var typed = provider.GetRequiredService<IOutcomeReader<Result<int>>>().Read(Result.Failure<int>(NotFound));
        var success = provider.GetRequiredService<IOutcomeReader<Result<int>>>().Read(Result.Success(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plain, Is.EqualTo(RequestOutcome.Failure("Node.NotFound")));
            Assert.That(typed, Is.EqualTo(RequestOutcome.Failure("Node.NotFound")));
            Assert.That(success.IsSuccess, Is.True);
        }
    }

    [Test]
    public void A_Default_Result_Should_Be_Read_As_A_Failure()
    {
        using var provider = Build();

        Assert.That(provider.GetRequiredService<IOutcomeReader<Result>>().Read(default).IsSuccess, Is.False);
    }

    [Test]
    public void A_Response_That_Is_Not_A_Result_Should_Read_As_A_Success_And_Get_No_Factory()
    {
        using var provider = Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetRequiredService<IOutcomeReader<string>>().Read("anything"), Is.EqualTo(RequestOutcome.Success));
            Assert.That(provider.GetRequiredService<IOutcomeReader<Unit>>().Read(Unit.Value).IsSuccess, Is.True);

            // Through IEnumerable: asking for the factory of a type it cannot build directly makes the container throw.
            Assert.That(provider.GetServices<IFailureFactory<string>>(), Is.Empty);
        }
    }

    [Test]
    public void A_Result_Type_That_Is_A_Class_Should_Be_Read_Too_And_A_Null_Is_A_Success()
    {
        using var provider = Build();
        var reader = provider.GetRequiredService<IOutcomeReader<ClassResult?>>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reader.Read(new ClassResult(false)), Is.EqualTo(RequestOutcome.Failure("Class.Failed")));
            Assert.That(reader.Read(new ClassResult(true)).IsSuccess, Is.True);
            Assert.That(reader.Read(null).IsSuccess, Is.True);
        }
    }

    [Test]
    public async Task A_Response_That_Is_Not_A_Result_Should_Work_With_The_Built_In_Behaviors()
    {
        var services = new ServiceCollection();
        services.AddCqrsResults();
        services.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<GreetHandler>();
            cqrs.UseTracing().UseMetrics();
        });
        await using var provider = services.BuildServiceProvider();

        Assert.That(await provider.GetRequiredService<ISender>().Send(new Greet()), Is.EqualTo("hello"));
    }

    [Test]
    public void A_Reader_Registered_For_A_Response_Type_Should_Win()
    {
        var own = new OwnReader();
        var services = new ServiceCollection();
        services.AddSingleton<IOutcomeReader<Result>>(own);
        services.AddCqrsResults();
        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IOutcomeReader<Result>>(), Is.SameAs(own));
    }

    [Test]
    public void The_Factory_Should_Build_A_Failed_Result_For_A_Validation_Refusal()
    {
        using var provider = Build();
        var refusal = new RequestFailure("Validation.Failed", "Name is required.", FailureKind.Validation);
        var expected = new Error("Validation.Failed", "Name is required.", ErrorType.Validation);

        var plain = provider.GetRequiredService<IFailureFactory<Result>>().Create(refusal);
        var typed = provider.GetRequiredService<IFailureFactory<Result<int>>>().Create(refusal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plain.IsFailure, Is.True);
            Assert.That(plain.Error, Is.EqualTo(expected));
            Assert.That(typed.IsFailure, Is.True);
            Assert.That(typed.Error, Is.EqualTo(expected));
        }
    }

    [Test]
    public void The_Factory_Should_Keep_The_Messages_Of_Each_Property_On_The_Error()
    {
        using var provider = Build();
        var errors = new Dictionary<string, string[]> { ["Name"] = ["Name is required.", "Name is too short."], ["Age"] = ["Age is required."] };
        var refusal = new RequestFailure("Validation.Failed", "Name is required. Age is required.", FailureKind.Validation) { Errors = errors };

        var result = provider.GetRequiredService<IFailureFactory<Result<int>>>().Create(refusal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error.Code, Is.EqualTo("Validation.Failed"));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(result.Error.Description, Is.EqualTo("Name is required. Age is required."));
            Assert.That(result.Error.Arguments.TryGetValue(CqrsResultArguments.Errors, out var argument), Is.True);
            Assert.That(argument.Kind, Is.EqualTo(ErrorArgumentKind.Data));
            Assert.That(argument.Value, Is.SameAs(errors));
        }
    }

    [Test]
    public void The_Factory_Should_Keep_The_Messages_When_The_Refusal_Is_Not_A_Validation()
    {
        using var provider = Build();
        var errors = new Dictionary<string, string[]> { ["Name"] = ["Name is taken."] };
        var refusal = new RequestFailure("Request.Refused", "Name is {taken}.", FailureKind.Failure) { Errors = errors };

        var result = provider.GetRequiredService<IFailureFactory<Result>>().Create(refusal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Failure));
            Assert.That(result.Error.Description, Is.EqualTo("Name is {taken}."));
            Assert.That(result.Error.Arguments.TryGetValue(CqrsResultArguments.Errors, out _), Is.True);
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void The_Factory_Should_Build_A_Plain_Error_When_There_Is_Nothing_To_Keep(int case_)
    {
        using var provider = Build();
        var refusal = case_ == 0
            ? new RequestFailure("Validation.Failed", "No.", FailureKind.Validation) { Errors = new Dictionary<string, string[]>() }
            : new RequestFailure("Validation.Failed", " ", FailureKind.Validation) { Errors = new Dictionary<string, string[]> { ["A"] = ["b"] } };

        var result = provider.GetRequiredService<IFailureFactory<Result>>().Create(refusal);

        Assert.That(result.Error.Arguments, Is.SameAs(ErrorArguments.Empty));
    }

    [Test]
    public void The_Factory_Should_Build_A_General_Failure_For_Any_Other_Refusal()
    {
        using var provider = Build();

        var result = provider.GetRequiredService<IFailureFactory<Result<string>>>()
            .Create(new RequestFailure("Request.Refused", "No.", FailureKind.Failure));

        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Failure));
    }

    [Test]
    public void The_Factory_Should_Say_So_For_A_Result_Type_It_Cannot_Build()
    {
        using var provider = Build();

        var factory = provider.GetRequiredService<IFailureFactory<OtherResult>>();

        Assert.That(() => factory.Create(new RequestFailure("A.B", "c", FailureKind.Failure)),
            Throws.InvalidOperationException.With.Message.Contains(nameof(OtherResult)));
    }

    [Test]
    public void AddCqrsResults_Should_Reject_Null_Services()
    {
        Assert.That(() => ResultsServiceCollectionExtensions.AddCqrsResults(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task A_Failed_Result_Should_Be_An_Error_Span_With_Its_Code()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CqrsTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddCqrsResults();
        services.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<FindNodeHandler>();
            cqrs.UseTracing();
        });
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new FindNode(false));

        var span = activities.Single(activity => activity.OperationName == nameof(FindNode));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(span.GetTagItem("cqrs.outcome"), Is.EqualTo("failure"));
            Assert.That(span.GetTagItem("cqrs.error.code"), Is.EqualTo("Node.NotFound"));
            Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Error));
        }
    }

    [Test]
    public async Task A_Behavior_Should_Be_Able_To_Refuse_A_Request_With_A_Failed_Result()
    {
        var services = new ServiceCollection();
        services.AddCqrsResults();
        services.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<FindNodeHandler>();
            cqrs.AddBehavior(typeof(RefuseAllBehavior<,>));
        });
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<ISender>().Send(new FindNode(true));

        Assert.That(result.Error.Code, Is.EqualTo("Request.Refused"));
    }

    private static ServiceProvider Build() => new ServiceCollection().AddCqrsResults().BuildServiceProvider();

    internal sealed record FindNode(bool Found) : IQuery<Result<int>>;

    internal sealed class FindNodeHandler : IRequestHandler<FindNode, Result<int>>
    {
        public ValueTask<Result<int>> Handle(FindNode request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(request.Found ? Result.Success(1) : Result.Failure<int>(NotFound));
    }

    internal sealed class RefuseAllBehavior<TRequest, TResponse>(IFailureFactory<TResponse> failures) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken) =>
            ValueTask.FromResult(failures.Create(new RequestFailure("Request.Refused", "Refused.", FailureKind.Failure)));
    }

    internal sealed record Greet : IQuery<string>;

    internal sealed class GreetHandler : IRequestHandler<Greet, string>
    {
        public ValueTask<string> Handle(Greet request, CancellationToken cancellationToken) => ValueTask.FromResult("hello");
    }

    internal sealed class ClassResult(bool success) : IResultState
    {
        public bool IsSuccess => success;

        public Error Error => success ? Error.None : new Error("Class.Failed", "It failed.", ErrorType.Failure);
    }

    private sealed class OwnReader : IOutcomeReader<Result>
    {
        public RequestOutcome Read(Result response) => RequestOutcome.Success;
    }

    internal readonly struct OtherResult : IResultState
    {
        public bool IsSuccess => true;

        public Error Error => Error.None;
    }
}
