using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sondor.AspNetCore;

namespace Sondor.Http.Tests;

/// <summary>
/// The client against the real server side: every problem <c>Sondor.AspNetCore</c> writes reads back to the same code, type and trace
/// id. This is what makes the wire contract a contract.
/// </summary>
[TestFixture]
public sealed class RoundTripTests
{
    private static readonly Guid OrderId = Guid.Parse("6f0e3a5c-0b0a-4c4f-9f4e-3f6c1d2a7b11");

    private WebApplication _app = null!;
    private HttpResultClient _client = null!;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSondorProblemDetails(options =>
        {
            options.TypeUriTemplate = "https://docs.example.com/errors/{code}";
            options.StatusByCode["Order.Late"] = 410;
        });

        _app = builder.Build();
        _app.UseSondorProblemDetails();

        _app.MapGet("/orders/{id:guid}", (Guid id) =>
            (id == OrderId ? Result.Success(new Order(id, "Widget")) : Result.Failure<Order>(Error.NotFound("Order", "Id"))).ToHttpResult());
        _app.MapGet("/late", () => Result.Failure<Order>(new Error("Order.Late", "The order is late.", ErrorType.Conflict)).ToHttpResult());
        _app.MapGet("/invalid", () => Result.Failure<Order>(Error.Invalid("Order", "Quantity")).ToHttpResult());
        _app.MapGet("/limited", () => Result.Failure<Order>(Error.RateLimited("search orders")).ToHttpResult());
        _app.MapGet("/disabled", () => Result.Failure<Order>(Error.FeatureDisabled("Order export")).ToHttpResult());
        _app.MapGet("/boom", Order () => throw new InvalidOperationException("secret internals"));
        _app.MapGet("/forbidden", () => Result.Failure<Order>(Error.Forbidden("cancel the order")).ToHttpResult());
        _app.MapGet("/unauthorized", () => Result.Failure<Order>(Error.Unauthorized("view orders")).ToHttpResult());
        _app.MapPost("/orders", (Order order) => Result.Success(order).ToHttpResult(created => TypedResults.Created($"/orders/{created.Id}", created)));
        _app.MapDelete("/orders/{id:guid}", (Guid id) => Result.Success().ToHttpResult());

        await _app.StartAsync();

        _client = new HttpResultClient(_app.GetTestClient());
    }

    [OneTimeTearDown]
    public async Task StopServer()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Test]
    public async Task GetAsync_Should_ReadAValueTheServerWrites()
    {
        var result = await _client.GetAsync<Order>($"/orders/{OrderId}");

        Assert.That(result.Value, Is.EqualTo(new Order(OrderId, "Widget")));
    }

    [Test]
    public async Task PostAsync_Should_ReadACreatedValue_AndDeleteAsync_ANoContent()
    {
        var created = await _client.PostAsync<Order, Order>("/orders", new Order(OrderId, "Widget"));
        var deleted = await _client.DeleteAsync($"/orders/{OrderId}");

        Assert.That(created.Value.Name, Is.EqualTo("Widget"));
        Assert.That(deleted.IsSuccess, Is.True);
    }

    [TestCase("/orders/00000000-0000-0000-0000-000000000001", "Resource.NotFound", ErrorType.NotFound, 404)]
    [TestCase("/late", "Order.Late", ErrorType.Failure, 410)]
    [TestCase("/invalid", "Resource.Invalid", ErrorType.Validation, 400)]
    [TestCase("/limited", "Request.RateLimited", ErrorType.RateLimited, 429)]
    [TestCase("/disabled", "Feature.Disabled", ErrorType.FeatureDisabled, 501)]
    [TestCase("/forbidden", "Request.Forbidden", ErrorType.Forbidden, 403)]
    [TestCase("/unauthorized", "Request.Unauthorized", ErrorType.Unauthorized, 401)]
    [TestCase("/boom", "Error.Internal", ErrorType.InternalServerError, 500)]
    [TestCase("/nowhere", "Request.NotFound", ErrorType.NotFound, 404)]
    public async Task GetAsync_Should_ReadBackTheCodeTheStatusAndTheTraceOfEveryProblemTheServerWrites(string path, string code, ErrorType type, int status)
    {
        var result = await _client.GetAsync<Order>(path);
        var failure = result.Error.GetHttpFailure();

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo(code));
        Assert.That(result.Error.Type, Is.EqualTo(type));
        Assert.That(failure!.Status, Is.EqualTo(status));
        Assert.That(failure.TraceId, Is.Not.Null.And.Not.Empty);
        Assert.That(failure.Problem!.ErrorCode, Is.EqualTo(code));
        Assert.That(failure.Problem.Status, Is.EqualTo(status));
        Assert.That(failure.Problem.Type, Does.StartWith("https://"));
    }

    [Test]
    public async Task GetAsync_Should_ReadTheDocumentationLinkAndTheInstance_TheServerWrites()
    {
        var failure = (await _client.GetAsync<Order>("/late")).Error.GetHttpFailure()!;

        Assert.That(failure.Problem!.Type, Is.EqualTo("https://docs.example.com/errors/Order.Late"));
        Assert.That(failure.Problem.Instance, Is.EqualTo("/late"));
        Assert.That(failure.Problem.Title, Is.Not.Empty);
    }

    [Test]
    public async Task GetAsync_Should_NeverSeeTheInternalsOfAnExceptionTheServerHandled()
    {
        var result = await _client.GetAsync<Order>("/boom");

        Assert.That(result.Error.Description, Does.Not.Contain("secret"));
        Assert.That(result.Error.GetHttpFailure()!.Problem!.Detail, Does.Not.Contain("secret"));
    }

    [Test]
    public async Task GetAsync_Should_UseTheServersDescription_WhenToldTo()
    {
        var client = new HttpResultClient(_app.GetTestClient(), new HttpResultOptions { UseRemoteDetail = true });

        var error = (await client.GetAsync<Order>("/late")).Error;

        Assert.That(error.Description, Is.EqualTo("The order is late."));
    }
}
