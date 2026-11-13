using System.Diagnostics;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using Sondor.Results;

namespace Sondor.Cqrs.FluentValidation.Tests;

[TestFixture]
public sealed class ValidationTests
{
    [Test]
    public async Task A_Request_Nobody_Validates_Should_Go_On_To_The_Handler()
    {
        await using var provider = Build();

        var response = await provider.GetRequiredService<ISender>().Send(new Plain(1));

        Assert.That(response, Is.EqualTo(2));
    }

    [Test]
    public async Task A_Valid_Request_Should_Go_On_To_The_Handler()
    {
        await using var provider = Build(services => services.AddTransient<IValidator<Register>, NameRequired>());

        var response = await provider.GetRequiredService<ISender>().Send(new Register("node"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo("registered node"));
            Assert.That(provider.GetRequiredService<Recorder>().Handled, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task An_Invalid_Request_Should_Throw_When_The_Response_Type_Has_No_Failure_Factory_And_Never_Reach_The_Handler()
    {
        await using var provider = Build(services => services.AddTransient<IValidator<Register>, NameRequired>());

        var exception = await Assert.ThrowsAsync<ValidationException>(async () => await provider.GetRequiredService<ISender>().Send(new Register("")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Errors.Select(error => error.PropertyName), Is.EqualTo(new List<string> { "Name" }));
            Assert.That(provider.GetRequiredService<Recorder>().Handled, Is.Zero);
        }
    }

    [Test]
    public async Task An_Invalid_Request_Should_Be_Refused_Through_The_Failure_Factory_When_There_Is_One()
    {
        await using var provider = Build(services =>
        {
            services.AddTransient<IValidator<Register>, NameRequired>();
            services.AddSingleton<IFailureFactory<string>, TextFailureFactory>();
        });

        var response = await provider.GetRequiredService<ISender>().Send(new Register(""));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response, Is.EqualTo("refused:Validation.Failed:Validation:Name is required."));
            Assert.That(provider.GetRequiredService<Recorder>().Handled, Is.Zero);
        }
    }

    [Test]
    public async Task Every_Validator_Should_Run_And_The_Messages_Should_Be_Joined_Without_Repeats()
    {
        await using var provider = Build(services =>
        {
            services.AddTransient<IValidator<Register>, NameRequired>();
            services.AddTransient<IValidator<Register>, NameRequiredAgain>();
            services.AddTransient<IValidator<Register>, NameShort>();
            services.AddSingleton<IFailureFactory<string>, TextFailureFactory>();
        });

        var response = await provider.GetRequiredService<ISender>().Send(new Register(""));

        Assert.That(response, Is.EqualTo("refused:Validation.Failed:Validation:Name is required."));

        var tooLong = await provider.GetRequiredService<ISender>().Send(new Register("far too long"));

        Assert.That(tooLong, Is.EqualTo("refused:Validation.Failed:Validation:Name is too long."));
    }

    [Test]
    public async Task The_Messages_Of_Different_Validators_Should_All_Be_Reported_In_Order()
    {
        await using var provider = Build(services =>
        {
            services.AddTransient<IValidator<Register>, NameShort>();
            services.AddTransient<IValidator<Register>, NameLetters>();
            services.AddSingleton<IFailureFactory<string>, TextFailureFactory>();
        });

        // A name that is too long and not letters fails both, and both messages are reported, in validator order.
        var response = await provider.GetRequiredService<ISender>().Send(new Register("toolong!"));

        Assert.That(response, Is.EqualTo("refused:Validation.Failed:Validation:Name is too long. Name must be letters."));
    }

    [Test]
    public async Task A_Failed_Result_Should_Refuse_An_Invalid_Request_When_Result_Support_Is_Registered()
    {
        await using var provider = Build(services =>
        {
            services.AddCqrsResults();
            services.AddTransient<IValidator<Create>, CreateValidator>();
        });

        var result = await provider.GetRequiredService<ISender>().Send(new Create(""));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error.Code, Is.EqualTo(CqrsValidation.ErrorCode));
            Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Validation));
            Assert.That(result.Error.Description, Is.EqualTo("A name is required."));
            Assert.That(provider.GetRequiredService<Recorder>().Handled, Is.Zero);
        }
    }

    [Test]
    public async Task A_Valid_Request_Should_Still_Return_The_Result_Of_The_Handler()
    {
        await using var provider = Build(services =>
        {
            services.AddCqrsResults();
            services.AddTransient<IValidator<Create>, CreateValidator>();
        });

        var result = await provider.GetRequiredService<ISender>().Send(new Create("node"));

        Assert.That(result.Value, Is.EqualTo(1));
    }

    [Test]
    public async Task An_Async_Validator_Should_Be_Awaited_And_Given_The_Cancellation_Token()
    {
        var seen = new List<CancellationToken>();
        await using var provider = Build(services =>
        {
            services.AddSingleton(seen);
            services.AddTransient<IValidator<Register>, SlowNameCheck>();
        });
        using var source = new CancellationTokenSource();

        await provider.GetRequiredService<ISender>().Send(new Register("node"), source.Token);

        Assert.That(seen, Is.EqualTo(new List<CancellationToken> { source.Token }));
    }

    [Test]
    public async Task The_Refused_Properties_Should_Be_Tagged_On_The_Span_And_Never_Their_Values()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CqrsTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var provider = Build(
            services =>
            {
                services.AddTransient<IValidator<Register>, NoSecrets>();
                services.AddSingleton<IFailureFactory<string>, TextFailureFactory>();
            },
            cqrs => cqrs.UseTracing());

        await provider.GetRequiredService<ISender>().Send(new Register("hunter2-secret"));

        var span = activities.Single(activity => activity.OperationName == nameof(Register));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(span.GetTagItem("cqrs.validation.properties"), Is.EqualTo("Name"));
            Assert.That(span.GetTagItem("cqrs.validation.error_count"), Is.EqualTo(1));
            Assert.That(span.TagObjects.Select(tag => tag.Value?.ToString()), Has.None.Contain("hunter2-secret"));
        }
    }

    [Test]
    public async Task The_Behavior_Should_Run_Where_It_Was_Added_In_The_Pipeline()
    {
        await using var provider = Build(
            services =>
            {
                services.AddTransient<IValidator<Register>, NameRequired>();
                services.AddSingleton<IFailureFactory<string>, TextFailureFactory>();
            },
            cqrs => cqrs.AddBehavior(typeof(OuterBehavior<,>)));

        await provider.GetRequiredService<ISender>().Send(new Register(""));

        // The behaviour added first is the outermost: it saw the request even though validation refused it.
        Assert.That(provider.GetRequiredService<Recorder>().Outer, Is.EqualTo(1));
    }

    [Test]
    public void UseFluentValidation_Should_Reject_A_Null_Builder()
    {
        Assert.That(() => FluentValidationBuilderExtensions.UseFluentValidation(null!), Throws.ArgumentNullException);
    }

    private static ServiceProvider Build(Action<IServiceCollection>? services = null, Action<CqrsBuilder>? configure = null)
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<Recorder>();
        services?.Invoke(collection);
        collection.AddCqrs(cqrs =>
        {
            cqrs.AddHandlersFromAssemblyContaining<RegisterHandler>();
            configure?.Invoke(cqrs);
            cqrs.UseFluentValidation();
        });

        return collection.BuildServiceProvider();
    }

    internal sealed class Recorder
    {
        public int Handled { get; set; }

        public int Outer { get; set; }
    }

    internal sealed record Register(string Name) : ICommand<string>;

    internal sealed class RegisterHandler(Recorder recorder) : IRequestHandler<Register, string>
    {
        public ValueTask<string> Handle(Register request, CancellationToken cancellationToken)
        {
            recorder.Handled++;
            return ValueTask.FromResult($"registered {request.Name}");
        }
    }

    internal sealed record Create(string Name) : ICommand<Result<int>>;

    internal sealed class CreateHandler(Recorder recorder) : IRequestHandler<Create, Result<int>>
    {
        public ValueTask<Result<int>> Handle(Create request, CancellationToken cancellationToken)
        {
            recorder.Handled++;
            return ValueTask.FromResult(Result.Success(1));
        }
    }

    internal sealed record Plain(int Value) : IQuery<int>;

    internal sealed class PlainHandler : IRequestHandler<Plain, int>
    {
        public ValueTask<int> Handle(Plain request, CancellationToken cancellationToken) => ValueTask.FromResult(request.Value + 1);
    }

    internal sealed class NameRequired : AbstractValidator<Register>
    {
        public NameRequired() => RuleFor(register => register.Name).NotEmpty().WithMessage("Name is required.");
    }

    internal sealed class NameRequiredAgain : AbstractValidator<Register>
    {
        public NameRequiredAgain() => RuleFor(register => register.Name).NotEmpty().WithMessage("Name is required.");
    }

    internal sealed class NameShort : AbstractValidator<Register>
    {
        public NameShort() => RuleFor(register => register.Name).MaximumLength(5).WithMessage("Name is too long.");
    }

    internal sealed class NameLetters : AbstractValidator<Register>
    {
        public NameLetters() => RuleFor(register => register.Name).Must(name => name.All(char.IsLetter)).WithMessage("Name must be letters.");
    }

    internal sealed class NoSecrets : AbstractValidator<Register>
    {
        public NoSecrets() => RuleFor(register => register.Name).Must(name => !name.Contains("secret", StringComparison.Ordinal)).WithMessage("Name is not allowed.");
    }

    internal sealed class CreateValidator : AbstractValidator<Create>
    {
        public CreateValidator() => RuleFor(create => create.Name).NotEmpty().WithMessage("A name is required.");
    }

    internal sealed class SlowNameCheck(List<CancellationToken> seen) : AbstractValidator<Register>
    {
        public override Task<ValidationResult> ValidateAsync(ValidationContext<Register> context, CancellationToken cancellation = default)
        {
            seen.Add(cancellation);
            return Task.FromResult(new ValidationResult());
        }
    }

    internal sealed class TextFailureFactory : IFailureFactory<string>
    {
        public string Create(RequestFailure failure) => $"refused:{failure.Code}:{failure.Kind}:{failure.Description}";
    }

    internal sealed class OuterBehavior<TRequest, TResponse>(Recorder recorder) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<TResponse> Handle(TRequest request, RequestContinuation<TResponse> continuation, CancellationToken cancellationToken)
        {
            recorder.Outer++;
            return continuation.Invoke();
        }
    }
}
