using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.AspNetCore.Tests;

[TestFixture]
public sealed partial class StatusCodeAndCustomizationTests
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex BareTraceIdPattern();

    [GeneratedRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$")]
    private static partial Regex TraceParentPattern();

    [Test]
    public async Task UseSondorProblemDetails_Should_GiveABareNotFound_AProblemBodyWithACode()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.GetAsync("/nothing-here");
        var problem = await Json.ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Request.NotFound"));
        Assert.That(problem.Text("title"), Is.EqualTo("Not Found"));
        Assert.That(problem.Text("traceId"), Is.Not.Empty);
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_GiveABareUnauthorized_AProblemBodyWithACode()
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/secret", () => TypedResults.Unauthorized()));

        var response = await app.Client.GetAsync("/secret");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await Json.ReadAsync(response)).Text("errorCode"), Is.EqualTo("Request.Unauthorized"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_UseTheGenericCode_ForAStatusWithoutOne()
    {
        await using var app = await TestApp.StartAsync(map: web =>
        {
            web.MapGet("/teapot", () => TypedResults.StatusCode(418));
            web.MapGet("/unavailable", () => TypedResults.StatusCode(503));
        });

        var teapot = await Json.ReadAsync(await app.Client.GetAsync("/teapot"));
        var unavailable = await Json.ReadAsync(await app.Client.GetAsync("/unavailable"));

        Assert.That(teapot.Text("errorCode"), Is.EqualTo("Request.Failed"));
        Assert.That(unavailable.Text("errorCode"), Is.EqualTo("Error.Internal"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_UseTheCodeForAStatus_When_ItWasChanged()
    {
        await using var app = await TestApp.StartAsync(options => options.CodeByStatus[404] = "Route.Unknown");

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/nothing-here"));

        Assert.That(problem.Text("errorCode"), Is.EqualTo("Route.Unknown"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_LinkAFrameworkProblem_ToTheDocumentation_When_ATemplateIsSet()
    {
        await using var app = await TestApp.StartAsync(options => options.TypeUriTemplate = "https://docs.example.com/errors/{code}");

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/nothing-here"));

        Assert.That(problem.Text("type"), Is.EqualTo("https://docs.example.com/errors/Request.NotFound"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_KeepATypeTheApplicationChose()
    {
        await using var app = await TestApp.StartAsync(
            options => options.TypeUriTemplate = "https://docs.example.com/errors/{code}",
            map: web => web.MapGet("/custom", () => TypedResults.Problem(statusCode: 409, type: "urn:example:custom")));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/custom"));

        Assert.That(problem.Text("type"), Is.EqualTo("urn:example:custom"));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Request.Conflict"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_LeaveABareStatusEmpty_When_BodiesAreOff()
    {
        await using var app = await TestApp.StartAsync(options => options.WriteStatusCodeBodies = false);

        var response = await app.Client.GetAsync("/nothing-here");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_LeaveAPageRequestsBareStatus_Alone()
    {
        await using var app = await TestApp.StartAsync(mvc: true);

        var page = await app.Client.GetAsync("/page/missing");
        var api = await app.Client.GetAsync("/api/nothing");

        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await page.Content.ReadAsStringAsync(), Is.Empty);
        Assert.That(api.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_LeaveABrowsersUnknownUrl_Alone()
    {
        await using var app = await TestApp.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/nothing-here");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

        var response = await app.Client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_AddACode_ToAnInvalidBodyAndKeepItsErrors()
    {
        await using var app = await TestApp.StartAsync(mvc: true);

        var response = await app.Client.PostAsync("/api/item", new StringContent("{}", new MediaTypeHeaderValue("application/json")));
        var problem = await Json.ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Request.Invalid"));
        Assert.That(problem.GetProperty("errors").EnumerateObject().Any(), Is.True);
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_AddACode_ToAnUnsupportedMediaType()
    {
        await using var app = await TestApp.StartAsync(mvc: true);

        var response = await app.Client.PostAsync("/api/item", new StringContent("x", new MediaTypeHeaderValue("text/plain")));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
        Assert.That((await Json.ReadAsync(response)).Text("errorCode"), Is.EqualTo("Request.UnsupportedMediaType"));
    }

    [Test]
    public async Task Customization_Should_KeepTheFrameworksTraceId_ByDefault()
    {
        using var listener = Listen(out _);

        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/fail", () => Result.Failure(Error.NotFound("Order", "Id")).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("traceId"), Does.Match(TraceParentPattern()));
    }

    [Test]
    public async Task Customization_Should_UseTheBareTraceId_When_Asked()
    {
        using var listener = Listen(out _);

        await using var app = await TestApp.StartAsync(
            options => options.BareTraceId = true,
            map: web => web.MapGet("/fail", () => Result.Failure(Error.NotFound("Order", "Id")).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("traceId"), Does.Match(BareTraceIdPattern()));
    }

    [Test]
    public async Task Customization_Should_TagTheRequestActivity_WithTheErrorCode()
    {
        using var listener = Listen(out var stopped);

        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/fail", () => Result.Failure(Error.NotFound("Order", "Id")).ToHttpResult()));

        await app.Client.GetAsync("/fail");

        var activity = await FirstTaggedAsync(stopped);

        Assert.That(activity.GetTagItem("error.type"), Is.EqualTo("Resource.NotFound"));
        Assert.That(activity.Status, Is.Not.EqualTo(ActivityStatusCode.Error), "a 404 is not a failed request");
    }

    [Test]
    public async Task Customization_Should_MarkTheActivityFailed_ForAServerError()
    {
        using var listener = Listen(out var stopped);

        await using var app = await TestApp.StartAsync(map: web =>
            web.MapGet("/boom", () => Fail.With(new InvalidOperationException("x"))));

        await app.Client.GetAsync("/boom");

        var activity = await FirstTaggedAsync(stopped);

        Assert.That(activity.GetTagItem("error.type"), Is.EqualTo("Error.Internal"));
        Assert.That(activity.Status, Is.EqualTo(ActivityStatusCode.Error));
    }

    [Test]
    public async Task Customization_Should_NotTagTheActivity_When_TaggingIsOff()
    {
        using var listener = Listen(out var stopped);

        await using var app = await TestApp.StartAsync(
            options => options.TagActivity = false,
            map: web => web.MapGet("/fail", () => Result.Failure(Error.NotFound("Order", "Id")).ToHttpResult()));

        await app.Client.GetAsync("/fail");
        await WaitAsync(() => stopped.Count > 0);

        Assert.That(stopped.Any(activity => activity.GetTagItem("error.type") is not null), Is.False);
    }

    [Test]
    public async Task Customization_Should_RunTheApplicationsOwnCustomization_AfterTheCodeIsSet()
    {
        await using var app = await TestApp.StartAsync(
            register: services => services.AddProblemDetails(options =>
                options.CustomizeProblemDetails = context =>
                    context.ProblemDetails.Extensions["seen-code"] = context.ProblemDetails.Extensions["errorCode"]),
            map: web => web.MapGet("/fail", () => Result.Failure(Error.NotFound("Order", "Id")).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("seen-code"), Is.EqualTo("Resource.NotFound"));
    }

    private static ActivityListener Listen(out List<Activity> stopped)
    {
        var list = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (list)
                {
                    list.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);
        stopped = list;

        return listener;
    }

    private static async Task<Activity> FirstTaggedAsync(List<Activity> stopped)
    {
        await WaitAsync(() => stopped.Any(activity => activity.GetTagItem("error.type") is not null));

        return stopped.First(activity => activity.GetTagItem("error.type") is not null);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }
}
