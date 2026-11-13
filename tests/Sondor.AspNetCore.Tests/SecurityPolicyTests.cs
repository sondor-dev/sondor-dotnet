using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.AspNetCore.Tests;

/// <summary>What the policies an application puts around its endpoints (a required header, a rate limit) look like when they reject.</summary>
[TestFixture]
public sealed class SecurityPolicyTests
{
    [Test]
    public async Task ToHttpResult_Should_ReportAnErrorFromAMiddleware_AndStopThePipeline()
    {
        var reached = false;

        await using var app = await TestApp.StartAsync(
            pipeline: web => web.Use(async (context, next) =>
            {
                if (!context.Request.Headers.ContainsKey("X-Required"))
                {
                    var error = new Error("Request.HeaderRequired", "The X-Required header is required.", ErrorType.Validation);

                    await error.ToHttpResult().ExecuteAsync(context);

                    return;
                }

                await next(context);
            }),
            map: web => web.MapGet("/ok", () =>
            {
                reached = true;

                return "ok";
            }));

        var rejected = await app.Client.GetAsync("/ok");
        var problem = await Json.ReadAsync(rejected);

        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(rejected.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Request.HeaderRequired"));
        Assert.That(problem.Text("detail"), Is.EqualTo("The X-Required header is required."));
        Assert.That(reached, Is.False);

        using var accepted = new HttpRequestMessage(HttpMethod.Get, "/ok");
        accepted.Headers.Add("X-Required", "1");

        Assert.That((await app.Client.SendAsync(accepted)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ToHttpResult_Should_ReportAnErrorWithAStatusOfItsOwn_When_TheCodeIsMapped()
    {
        var error = Error.Forbidden("use this client");

        await using var app = await TestApp.StartAsync(
            options => options.StatusByCode[error.Code] = 451,
            pipeline: web => web.Use((HttpContext context, RequestDelegate _) => error.ToHttpResult().ExecuteAsync(context)));

        var response = await app.Client.GetAsync("/anything");

        Assert.That((int)response.StatusCode, Is.EqualTo(451));
    }

    [Test]
    public async Task RateLimiter_Should_BeReportedAsRateLimited_When_ItRejectsWith429()
    {
        await using var app = await RateLimitedAsync(StatusCodes.Status429TooManyRequests);

        await app.Client.GetAsync("/limited");
        var rejected = await app.Client.GetAsync("/limited");
        var problem = await Json.ReadAsync(rejected);

        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(rejected.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Request.RateLimited"));
    }

    [Test]
    public async Task RateLimiter_Should_BeReportedAsUnavailable_When_ItKeepsTheFrameworksDefaultStatus()
    {
        await using var app = await RateLimitedAsync(StatusCodes.Status503ServiceUnavailable);

        await app.Client.GetAsync("/limited");
        var rejected = await app.Client.GetAsync("/limited");

        Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await Json.ReadAsync(rejected)).Text("errorCode"), Is.EqualTo("Request.Unavailable"));
    }

    [TestCase(503, "Request.Unavailable")]
    [TestCase(504, "Request.GatewayTimeout")]
    public async Task UseSondorProblemDetails_Should_GiveAGatewayStatus_ItsOwnCode(int status, string code)
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/status", () => TypedResults.StatusCode(status)));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/status"));

        Assert.That(problem.Text("errorCode"), Is.EqualTo(code));
    }

    [Test]
    public async Task ExceptionHandler_Should_KeepAHeaderAddedWithOnStarting_OnTheProblemResponse()
    {
        await using var app = await TestApp.StartAsync(
            usePackage: false,
            pipeline: web =>
            {
                web.Use((context, next) =>
                {
                    context.Response.OnStarting(() =>
                    {
                        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

                        return Task.CompletedTask;
                    });

                    return next(context);
                });

                web.UseSondorProblemDetails();
            },
            map: web => web.MapGet("/boom", () => Fail.With(new InvalidOperationException("x"))));

        var response = await app.Client.GetAsync("/boom");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
    }

    private static Task<TestApp> RateLimitedAsync(int rejectionStatus) =>
        TestApp.StartAsync(
            register: services => services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = rejectionStatus;
                options.AddFixedWindowLimiter("one", limiter =>
                {
                    limiter.PermitLimit = 1;
                    limiter.Window = TimeSpan.FromMinutes(1);
                });
            }),
            pipeline: web => web.UseRateLimiter(),
            map: web => web.MapGet("/limited", () => "ok").RequireRateLimiting("one"));
}
