using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sondor.AspNetCore.Tests;

[TestFixture]
public sealed class ExceptionHandlingTests
{
    [Test]
    public async Task UseSondorProblemDetails_Should_AnswerAGeneric500_AndNeverShowTheMessage()
    {
        await using var app = await TestApp.StartAsync(map: web =>
            web.MapGet("/boom", () => Fail.With(new InvalidOperationException("secret connection string"))));

        var response = await app.Client.GetAsync("/boom");
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(body, Does.Not.Contain("secret"));
        Assert.That(body, Does.Contain("\"errorCode\":\"Error.Internal\""));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_LogTheException_When_ItIsUnexpected()
    {
        await using var app = await TestApp.StartAsync(map: web =>
            web.MapGet("/boom", () => Fail.With(new InvalidOperationException("secret connection string"))));

        await app.Client.GetAsync("/boom");

        var entry = app.Logs.Single(log => log.Level == LogLevel.Error && log.Category.EndsWith("ErrorExceptionHandler", StringComparison.Ordinal));
        Assert.That(entry.Exception?.Message, Is.EqualTo("secret connection string"));
        Assert.That(entry.Message, Does.Contain("500").And.Contain("Error.Internal"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_ShowTheMessage_When_ExceptionDetailsAreIncluded()
    {
        await using var app = await TestApp.StartAsync(
            options => options.IncludeExceptionDetails = true,
            map: web => web.MapGet("/boom", () => Fail.With(new InvalidOperationException("visible while developing"))));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/boom"));

        Assert.That(problem.Text("detail"), Is.EqualTo("visible while developing"));
        Assert.That(problem.Text("exception"), Is.EqualTo(typeof(InvalidOperationException).FullName));
    }

    [Test]
    public async Task Map_Should_AnswerAMappedException_WithItsError()
    {
        await using var app = await TestApp.StartAsync(
            options => options.Exceptions.Map<PaymentDeclinedException>(exception =>
                new Error("Payment.Declined", exception.Message, ErrorType.Conflict)),
            map: web => web.MapGet("/pay", () => Fail.With(new PaymentDeclinedException("The card was declined."))));

        var response = await app.Client.GetAsync("/pay");
        var problem = await Json.ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Payment.Declined"));
        Assert.That(problem.Text("detail"), Is.EqualTo("The card was declined."));
    }

    [Test]
    public async Task Map_Should_LogAMappedClientError_AsAWarning()
    {
        await using var app = await TestApp.StartAsync(
            options => options.Exceptions.Map<PaymentDeclinedException>(_ => new Error("Payment.Declined", "Declined.", ErrorType.Conflict)),
            map: web => web.MapGet("/pay", () => Fail.With(new PaymentDeclinedException("x"))));

        await app.Client.GetAsync("/pay");

        Assert.That(app.Logs.Count(log => log.Level == LogLevel.Warning && log.Category.EndsWith("ErrorExceptionHandler", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(app.Logs.Any(log => log.Level == LogLevel.Error), Is.False);
    }

    [Test]
    public async Task Map_Should_UseTheNearestMappedBaseType_AndTheExactTypeFirst()
    {
        await using var app = await TestApp.StartAsync(
            options => options.Exceptions
                .Map<PaymentDeclinedException>(_ => new Error("Payment.Declined", "Declined.", ErrorType.Conflict))
                .Map<CardExpiredException>(_ => new Error("Payment.CardExpired", "Expired.", ErrorType.Validation)),
            map: web =>
            {
                web.MapGet("/expired", () => Fail.With(new CardExpiredException("x")));
                web.MapGet("/declined", () => Fail.With(new PaymentDeclinedException("x")));
            });

        var expired = await Json.ReadAsync(await app.Client.GetAsync("/expired"));
        var declined = await Json.ReadAsync(await app.Client.GetAsync("/declined"));

        Assert.That(expired.Text("errorCode"), Is.EqualTo("Payment.CardExpired"));
        Assert.That(declined.Text("errorCode"), Is.EqualTo("Payment.Declined"));
    }

    [Test]
    public void Map_Should_ReplaceAnEarlierMapping_AndRejectNull()
    {
        var mappings = new ExceptionMappings()
            .Map<PaymentDeclinedException>(_ => new Error("A.First", "First.", ErrorType.Failure))
            .Map<PaymentDeclinedException>(_ => new Error("A.Second", "Second.", ErrorType.Failure));

        Assert.That(mappings.TryMap(new PaymentDeclinedException("x"), out var error), Is.True);
        Assert.That(error.Code, Is.EqualTo("A.Second"));
        Assert.That(mappings.TryMap(new InvalidOperationException(), out _), Is.False);
        Assert.That(() => mappings.Map<PaymentDeclinedException>(null!), Throws.ArgumentNullException);
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_LeaveTheException_When_HandlingIsOff()
    {
        await using var app = await TestApp.StartAsync(
            options => options.HandleExceptions = false,
            map: web => web.MapGet("/boom", () => Fail.With(new InvalidOperationException("not handled here"))));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client.GetAsync("/boom"));

        Assert.That(thrown!.Message, Is.EqualTo("not handled here"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_StillAnswerInJson_When_TheCallerAcceptsSomethingElse()
    {
        await using var app = await TestApp.StartAsync(map: web =>
            web.MapGet("/boom", () => Fail.With(new InvalidOperationException("x"))));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        var response = await app.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That((await Json.ReadAsync(response)).Text("errorCode"), Is.EqualTo("Error.Internal"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_AnswerAnApiController_AsAProblem()
    {
        await using var app = await TestApp.StartAsync(mvc: true);

        var response = await app.Client.GetAsync("/api/throw");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("secret"));
    }

    [Test]
    public async Task ExceptionHandler_Should_LeaveAPageRequestToTheApplicationsErrorPage()
    {
        await using var app = await TestApp.StartAsync(
            usePackage: false,
            mvc: true,
            pipeline: web => web.UseExceptionHandler("/oops"),
            map: web => web.MapGet("/oops", () => "the application's error page"));

        var page = await app.Client.GetAsync("/page/throw");
        var api = await app.Client.GetAsync("/api/throw");

        Assert.That(await page.Content.ReadAsStringAsync(), Is.EqualTo("the application's error page"));
        Assert.That(api.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task ExceptionHandler_Should_TreatAMinimalApi_AsAnApi_EvenForABrowser()
    {
        await using var app = await TestApp.StartAsync(map: web =>
            web.MapGet("/boom", () => Fail.With(new InvalidOperationException("x"))));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/boom");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        var response = await app.Client.SendAsync(request);

        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task ExceptionHandler_Should_Decline_When_TheRequestIsDeclinedByAppliesTo()
    {
        await using var app = await TestApp.StartAsync(
            options => options.AppliesTo = _ => false,
            map: web => web.MapGet("/boom", () => Fail.With(new InvalidOperationException("x"))));

        var response = await app.Client.GetAsync("/boom");
        var problem = await Json.ReadAsync(response);

        // The framework's own exception handler answers a declined request, not this package.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(problem.TryGetProperty("detail", out _), Is.False);
    }

    [Test]
    public async Task TryHandleAsync_Should_Fail_When_TheErrorHandlingWasNotRegistered()
    {
        using var host = HandlerHost.Create(register: services =>
            services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(IErrorProblemFactory))));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => host.HandleAsync(new InvalidOperationException("x")));

        Assert.That(thrown!.Message, Does.Contain("AddSondorProblemDetails"));
    }

    // The cases below need an exception shape or a caller state a request cannot easily produce, so they call the handler directly.

    [Test]
    public async Task TryHandleAsync_Should_AnswerTheStatusConfigured_And_NotLogAnError_When_TheCallerWentAway()
    {
        using var host = HandlerHost.Create();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var (handled, context) = await host.HandleAsync(new OperationCanceledException(), aborted: cancelled.Token);

        Assert.That(handled, Is.True);
        Assert.That(context.Response.StatusCode, Is.EqualTo(499));
        Assert.That(host.Logs.Any(log => log.Level >= LogLevel.Warning), Is.False);
        Assert.That(host.Logs.Any(log => log.Level == LogLevel.Debug), Is.True);
    }

    [Test]
    public async Task TryHandleAsync_Should_UseTheConfiguredCancelledStatus()
    {
        using var host = HandlerHost.Create(options => options.ClientClosedRequestStatus = 408);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var (_, context) = await host.HandleAsync(new OperationCanceledException(), aborted: cancelled.Token);

        Assert.That(context.Response.StatusCode, Is.EqualTo(408));
    }

    [Test]
    public async Task TryHandleAsync_Should_TreatACancellationAsUnexpected_When_TheCallerStayed()
    {
        using var host = HandlerHost.Create();

        var (_, context) = await host.HandleAsync(new OperationCanceledException("a timeout inside the service"));

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task TryHandleAsync_Should_KeepTheStatusOfABadHttpRequest_AndHideItsMessage()
    {
        using var host = HandlerHost.Create();

        var (_, context) = await host.HandleAsync(new BadHttpRequestException("Request body too large. secret detail", StatusCodes.Status413PayloadTooLarge));
        var body = await HandlerHost.ReadAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(413));
        Assert.That(body, Does.Contain("Request.Invalid"));
        Assert.That(body, Does.Not.Contain("secret"));
    }

    [Test]
    public async Task TryHandleAsync_Should_UnwrapASingleAggregateAndAnInvocationException()
    {
        using var host = HandlerHost.Create(options =>
            options.Exceptions.Map<PaymentDeclinedException>(_ => new Error("Payment.Declined", "Declined.", ErrorType.Conflict)));

        var wrapped = new AggregateException(new TargetInvocationException(new PaymentDeclinedException("x")));

        var (_, context) = await host.HandleAsync(wrapped);

        Assert.That(context.Response.StatusCode, Is.EqualTo(409));
        Assert.That(await HandlerHost.ReadAsync(context), Does.Contain("Payment.Declined"));
    }

    [Test]
    public async Task TryHandleAsync_Should_KeepAnAggregateOfSeveralExceptions_AsOne()
    {
        using var host = HandlerHost.Create(options =>
            options.Exceptions.Map<PaymentDeclinedException>(_ => new Error("Payment.Declined", "Declined.", ErrorType.Conflict)));

        var several = new AggregateException(new PaymentDeclinedException("a"), new PaymentDeclinedException("b"));

        var (_, context) = await host.HandleAsync(several);

        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
    }

    [Test]
    public async Task TryHandleAsync_Should_ReturnFalse_When_TheRequestIsNotAnApiRequest()
    {
        using var host = HandlerHost.Create(options => options.AppliesTo = _ => false);

        var (handled, context) = await host.HandleAsync(new InvalidOperationException("x"));

        Assert.That(handled, Is.False);
        Assert.That(context.Response.StatusCode, Is.EqualTo(200));
    }
}
