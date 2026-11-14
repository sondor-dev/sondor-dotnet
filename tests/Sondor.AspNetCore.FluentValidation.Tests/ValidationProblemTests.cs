using System.Net;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sondor.AspNetCore.Internal;
using Sondor.Cqrs;
using Sondor.Results;

namespace Sondor.AspNetCore.FluentValidation.Tests;

[TestFixture]
public sealed class ValidationProblemTests
{
    private static readonly ValidationFailure[] Failures =
    [
        new("Name", "A name is required."),
        new("Name", "A name is too short."),
        new("Name", "A name is too short."),
        new("Address.PostCode", "The post code is not valid."),
        new("Items[0].UnitPrice", "The price must be positive."),
        new(string.Empty, "The order is not valid."),
    ];

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_AnswerA400WithErrorsByProperty()
    {
        await using var app = await StartAsync(map: web => web.MapGet("/", () => Throw(new ValidationException(Failures))));

        var response = await app.GetAsync("/");
        var problem = await ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(problem.GetProperty("errorCode").GetString(), Is.EqualTo("Validation.Failed"));
        Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo(400));
        Assert.That(problem.TryGetProperty("traceId", out _), Is.True);

        var errors = problem.GetProperty("errors");
        Assert.That(string.Join("|", Messages(errors, "name")), Is.EqualTo(string.Join("|", "A name is required.", "A name is too short.")));
        Assert.That(string.Join("|", Messages(errors, "address.postCode")), Is.EqualTo(string.Join("|", "The post code is not valid.")));
        Assert.That(string.Join("|", Messages(errors, "items[0].unitPrice")), Is.EqualTo(string.Join("|", "The price must be positive.")));
        Assert.That(string.Join("|", Messages(errors, string.Empty)), Is.EqualTo(string.Join("|", "The order is not valid.")));
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_KeepPropertyNames_When_NoNamingPolicyIsConfigured()
    {
        await using var app = await StartAsync(
            register: services => services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = null),
            map: web => web.MapGet("/", () => Throw(new ValidationException(Failures))));

        var errors = (await ReadAsync(await app.GetAsync("/"))).GetProperty("errors");

        Assert.That(Messages(errors, "Address.PostCode"), Has.Length.EqualTo(1));
        Assert.That(Messages(errors, "Items[0].UnitPrice"), Has.Length.EqualTo(1));
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_UseTheStatusOfTheValidationType()
    {
        await using var app = await StartAsync(
            configure: options => options.StatusByType[ErrorType.Validation] = 422,
            map: web => web.MapGet("/", () => Throw(new ValidationException(Failures))));

        var response = await app.GetAsync("/");

        Assert.That((int)response.StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_AnswerAnEmptyErrors_When_TheExceptionHasNoFailures()
    {
        await using var app = await StartAsync(map: web => web.MapGet("/", () => Throw(new ValidationException("invalid"))));

        var problem = await ReadAsync(await app.GetAsync("/"));

        Assert.That(problem.GetProperty("errors").EnumerateObject().Any(), Is.False);
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_NotAddErrors_When_TheExceptionIsAnotherKind()
    {
        await using var app = await StartAsync(map: web => web.MapGet("/", () => Throw(new InvalidOperationException("secret"))));

        var response = await app.GetAsync("/");
        var problem = await ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(problem.TryGetProperty("errors", out _), Is.False);
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_NotAddErrors_When_AResultFails()
    {
        await using var app = await StartAsync(map: web =>
            web.MapGet("/", () => Result.Failure(Error.Invalid("Order", "Quantity")).ToHttpResult()));

        var problem = await ReadAsync(await app.GetAsync("/"));

        Assert.That(problem.GetProperty("errorCode").GetString(), Is.EqualTo("Resource.Invalid"));
        Assert.That(problem.TryGetProperty("errors", out _), Is.False);
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_GiveWayToYourOwnMapping_When_ItComesAfter()
    {
        await using var app = await StartAsync(
            register: services => services.Configure<ErrorResponseOptions>(options =>
                options.Exceptions.Map<ValidationException>(_ => new Error("Order.Invalid", "Mine.", ErrorType.Conflict))),
            map: web => web.MapGet("/", () => Throw(new ValidationException(Failures))));

        var response = await app.GetAsync("/");
        var problem = await ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem.GetProperty("errorCode").GetString(), Is.EqualTo("Order.Invalid"));
        Assert.That(problem.TryGetProperty("errors", out _), Is.True);
    }

    [Test]
    public void AddSondorFluentValidationProblems_Should_RegisterOnce_When_CalledTwice()
    {
        var services = new ServiceCollection();

        services.AddSondorFluentValidationProblems().AddSondorFluentValidationProblems();

        Assert.That(services.Count(d => d.ServiceType == typeof(IProblemContributor)), Is.EqualTo(1));
        Assert.That(services.Count(d => d.ServiceType == typeof(IConfigureOptions<ErrorResponseOptions>)), Is.EqualTo(1));
    }

    [Test]
    public void AddSondorFluentValidationProblems_Should_Throw_When_ServicesIsNull() =>
        Assert.That(() => FluentValidationProblemsExtensions.AddSondorFluentValidationProblems(null!), Throws.ArgumentNullException);

    [TestCase("Name", "name")]
    [TestCase("Address.PostCode", "address.postCode")]
    [TestCase("Items[0].UnitPrice", "items[0].unitPrice")]
    [TestCase("Matrix[1][2]", "matrix[1][2]")]
    [TestCase("", "")]
    public void Name_Should_ConvertEachNameOfThePath(string path, string expected) =>
        Assert.That(ValidationProblemContributor.Name(path, JsonNamingPolicy.CamelCase), Is.EqualTo(expected));

    [Test]
    public void Name_Should_KeepThePath_When_ThereIsNoPolicy() =>
        Assert.That(ValidationProblemContributor.Name("Items[0].UnitPrice", null), Is.EqualTo("Items[0].UnitPrice"));

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_AnswerErrorsByProperty_When_ARefusedRequestReturnsAResult()
    {
        await using var app = await StartAsync(
            register: services =>
            {
                services.AddTransient<IValidator<Register>, RegisterValidator>();
                services.AddCqrsResults();
                services.AddCqrs(cqrs =>
                {
                    cqrs.AddHandlersFromAssemblyContaining<RegisterHandler>();
                    cqrs.UseFluentValidation();
                });
            },
            map: web => web.MapPost("/register", async (ISender sender) =>
                (await sender.Send(new Register(string.Empty, "x"))).ToHttpResult()));

        var response = await app.PostAsync("/register");
        var problem = await ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem.GetProperty("errorCode").GetString(), Is.EqualTo("Validation.Failed"));
        Assert.That(problem.GetProperty("detail").GetString(), Does.Contain("A name is required."));

        var errors = problem.GetProperty("errors");
        Assert.That(string.Join("|", Messages(errors, "name")), Is.EqualTo("A name is required."));
        Assert.That(string.Join("|", Messages(errors, "nickName")), Is.EqualTo("A nick name is too short."));
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_MergeMessages_When_TwoPropertiesShareAName()
    {
        var byProperty = new Dictionary<string, string[]> { ["Name"] = ["One."], ["name"] = ["One.", "Two."] };

        await using var app = await StartAsync(map: web => web.MapGet("/", () => WithErrors(byProperty)));

        var errors = (await ReadAsync(await app.GetAsync("/"))).GetProperty("errors");

        Assert.That(string.Join("|", Messages(errors, "name")), Is.EqualTo("One.|Two."));
    }

    [Test]
    public async Task AddSondorFluentValidationProblems_Should_NotAddErrors_When_TheArgumentIsNotTheMessagesOfProperties()
    {
        await using var app = await StartAsync(map: web => web.MapGet("/", () => WithErrors("not a dictionary")));

        var problem = await ReadAsync(await app.GetAsync("/"));

        Assert.That(problem.TryGetProperty("errors", out _), Is.False);
    }

    private static IResult WithErrors(object value) =>
        Result.Failure(ErrorTemplates.Invalid
                .WithCode("Order.Invalid")
                .Create(
                    ErrorArgument.Term(ErrorArgumentNames.Entity, "Order"),
                    ErrorArgument.Term(ErrorArgumentNames.Property, "Total"),
                    ErrorArgument.Data("Errors", value)))
            .ToHttpResult();

    internal sealed record Register(string Name, string NickName) : ICommand<Result>;

    internal sealed class RegisterHandler : IRequestHandler<Register, Result>
    {
        public ValueTask<Result> Handle(Register request, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success());
    }

    internal sealed class RegisterValidator : AbstractValidator<Register>
    {
        public RegisterValidator()
        {
            RuleFor(register => register.Name).NotEmpty().WithMessage("A name is required.");
            RuleFor(register => register.NickName).MinimumLength(3).WithMessage("A nick name is too short.");
        }
    }

    private static IResult Throw(Exception exception) => throw exception;

    private static string[] Messages(JsonElement errors, string property) =>
        [.. errors.GetProperty(property).EnumerateArray().Select(message => message.GetString()!)];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<TestApp> StartAsync(
        Action<ErrorResponseOptions>? configure = null,
        Action<IServiceCollection>? register = null,
        Action<WebApplication>? map = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSondorProblemDetails(configure).AddSondorFluentValidationProblems();
        register?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseSondorProblemDetails();
        map?.Invoke(app);
        await app.StartAsync();

        return new TestApp(app);
    }

    private sealed class TestApp(WebApplication app) : IAsyncDisposable
    {
        private readonly HttpClient _client = app.GetTestClient();

        public Task<HttpResponseMessage> GetAsync(string path) => _client.GetAsync(path);

        public Task<HttpResponseMessage> PostAsync(string path) => _client.PostAsync(path, null);

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
