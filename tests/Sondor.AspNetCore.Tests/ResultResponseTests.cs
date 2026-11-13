using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.AspNetCore.Tests;

[TestFixture]
public sealed class ResultResponseTests
{
    private static readonly Error OrderMissing = Error.NotFound("Order", "Id");

    [Test]
    public async Task ToHttpResult_Should_Answer204_When_ASuccessHasNoValue()
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/done", () => Result.Success().ToHttpResult()));

        var response = await app.Client.GetAsync("/done");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task ToHttpResult_Should_Answer200WithTheValue_When_ASuccessHasOne()
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/value", () => Result.Success(new { Id = 7 }).ToHttpResult()));

        var response = await app.Client.GetAsync("/value");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Json.ReadAsync(response)).GetProperty("id").GetInt32(), Is.EqualTo(7));
    }

    [Test]
    public async Task ToHttpResult_Should_UseOnSuccess_When_ItIsGiven()
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/created", () =>
            Result.Success(7).ToHttpResult(id => TypedResults.Created($"/orders/{id}", id))));

        var response = await app.Client.GetAsync("/created");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo("/orders/7"));
    }

    [Test]
    public async Task ToHttpResult_Should_NotCallOnSuccess_When_TheResultFailed()
    {
        var called = false;

        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/failed", () =>
            Result.Failure<int>(OrderMissing).ToHttpResult(_ =>
            {
                called = true;

                return TypedResults.Ok();
            })));

        var response = await app.Client.GetAsync("/failed");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(called, Is.False);
    }

    [Test]
    public async Task ToHttpResult_Should_WriteAProblemWithEveryMember_When_AResultFailed()
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/orders/{id}", (string id) => Result.Failure(OrderMissing).ToHttpResult()));

        var response = await app.Client.GetAsync("/orders/12");
        var problem = await Json.ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo(404));
        Assert.That(problem.Text("title"), Is.EqualTo("Not Found"));
        Assert.That(problem.Text("detail"), Is.EqualTo(OrderMissing.Description));
        Assert.That(problem.Text("instance"), Is.EqualTo("/orders/12"));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Resource.NotFound"));
        Assert.That(problem.Text("type"), Does.StartWith("https://tools.ietf.org/html/rfc9110#"));
        Assert.That(problem.Text("traceId"), Is.Not.Empty);
    }

    [TestCase(ErrorType.Validation, 400)]
    [TestCase(ErrorType.Unauthorized, 401)]
    [TestCase(ErrorType.Forbidden, 403)]
    [TestCase(ErrorType.NotFound, 404)]
    [TestCase(ErrorType.Conflict, 409)]
    [TestCase(ErrorType.RateLimited, 429)]
    [TestCase(ErrorType.InternalServerError, 500)]
    [TestCase(ErrorType.Failure, 500)]
    [TestCase(ErrorType.FeatureDisabled, 501)]
    public async Task ToHttpResult_Should_UseTheDefaultStatus_ForEachErrorType(ErrorType type, int expected)
    {
        var error = new Error("Area.Reason", "Something is wrong.", type);

        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/fail", () => Result.Failure(error).ToHttpResult()));

        var response = await app.Client.GetAsync("/fail");

        Assert.That((int)response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task ToHttpResult_Should_UseTheStatusForTheType_When_ItWasChanged()
    {
        await using var app = await TestApp.StartAsync(
            options => options.StatusByType[ErrorType.Validation] = 422,
            map: web => web.MapGet("/fail", () => Result.Failure(Error.Invalid("Order", "Id")).ToHttpResult()));

        var response = await app.Client.GetAsync("/fail");

        Assert.That((int)response.StatusCode, Is.EqualTo(422));
    }

    [Test]
    public async Task ToHttpResult_Should_PreferTheStatusForTheCode_OverTheStatusForTheType()
    {
        await using var app = await TestApp.StartAsync(
            options => options.StatusByCode["Resource.NotFound"] = 410,
            map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        var response = await app.Client.GetAsync("/fail");

        Assert.That((int)response.StatusCode, Is.EqualTo(410));
    }

    [Test]
    public async Task ToHttpResult_Should_LinkTheTypeToTheDocumentation_When_ATemplateIsSet()
    {
        var error = new Error("Order Late/1", "The order is late.", ErrorType.Conflict);

        await using var app = await TestApp.StartAsync(
            options => options.TypeUriTemplate = "https://docs.example.com/errors/{code}",
            map: web => web.MapGet("/fail", () => Result.Failure(error).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("type"), Is.EqualTo("https://docs.example.com/errors/Order%20Late%2F1"));
    }

    [Test]
    public async Task ToHttpResult_Should_NameTheCodeMemberAsConfigured()
    {
        await using var app = await TestApp.StartAsync(
            options => options.ErrorCodeMember = "code",
            map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("code"), Is.EqualTo("Resource.NotFound"));
        Assert.That(problem.TryGetProperty("errorCode", out _), Is.False);
    }

    [Test]
    public async Task ToHttpResult_Should_StillWriteAProblem_When_TheCallerAsksForHtml()
    {
        await using var app = await TestApp.StartAsync(map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/fail");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await app.Client.SendAsync(request);
        var problem = await Json.ReadAsync(response);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Resource.NotFound"));
    }

    [Test]
    public async Task ToHttpResult_Should_Fail_When_TheErrorHandlingWasNotRegistered()
    {
        await using var app = await TestApp.StartAsync(
            addPackage: false,
            usePackage: false,
            map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Client.GetAsync("/fail"));

        Assert.That(thrown!.Message, Does.Contain("AddSondorProblemDetails"));
    }

    [Test]
    public async Task ToHttpResult_Should_WorkFromAControllerAction_ThatReturnsAnIResult()
    {
        await using var app = await TestApp.StartAsync(mvc: true);

        var failure = await app.Client.GetAsync("/api/missing");
        var success = await app.Client.GetAsync("/api/ok");

        Assert.That(failure.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Json.ReadAsync(failure)).Text("errorCode"), Is.EqualTo("Resource.NotFound"));
        Assert.That(success.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Create_Should_UseTheTextProvider_When_OneIsRegistered()
    {
        await using var app = await TestApp.StartAsync(
            register: services => services.AddSingleton<IProblemTextProvider, ShoutingText>(),
            map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("title"), Is.EqualTo("NOT FOUND"));
        Assert.That(problem.Text("detail"), Is.EqualTo(OrderMissing.Description.ToUpperInvariant()));
        Assert.That(problem.Text("errorCode"), Is.EqualTo("Resource.NotFound"), "the code is never translated");
    }

    [Test]
    public async Task Create_Should_KeepTheEnglishText_When_TheTextProviderHasNone()
    {
        await using var app = await TestApp.StartAsync(
            register: services => services.AddSingleton<IProblemTextProvider, NoText>(),
            map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("detail"), Is.EqualTo(OrderMissing.Description));
    }

    [Test]
    public async Task Create_Should_RunEveryContributor_InTheOrderRegistered()
    {
        await using var app = await TestApp.StartAsync(
            register: services =>
            {
                services.AddSingleton<IProblemContributor, AddMember>();
                services.AddSingleton<IProblemContributor, OverwriteMember>();
            },
            map: web => web.MapGet("/fail", () => Result.Failure(OrderMissing).ToHttpResult()));

        var problem = await Json.ReadAsync(await app.Client.GetAsync("/fail"));

        Assert.That(problem.Text("code-seen"), Is.EqualTo("Resource.NotFound"));
        Assert.That(problem.Text("order"), Is.EqualTo("second"));
    }

    [Test]
    public void ProblemContext_Should_RejectMissingParts()
    {
        var problem = new ProblemDetails();

        Assert.That(() => new ProblemContext(null!, problem, OrderMissing), Throws.ArgumentNullException);
        Assert.That(() => new ProblemContext(new DefaultHttpContext(), null!, OrderMissing), Throws.ArgumentNullException);
    }

    private sealed class ShoutingText : IProblemTextProvider
    {
        public ProblemText? GetText(Error failure, HttpContext context) =>
            new(ReasonPhrase(failure).ToUpperInvariant(), failure.Description.ToUpperInvariant());

        private static string ReasonPhrase(Error failure) => failure.Type == ErrorType.NotFound ? "Not Found" : "Error";
    }

    private sealed class NoText : IProblemTextProvider
    {
        public ProblemText? GetText(Error failure, HttpContext context) => null;
    }

    private sealed class AddMember : IProblemContributor
    {
        public void Contribute(ProblemContext context)
        {
            context.Problem.Extensions["code-seen"] = context.Error.Code;
            context.Problem.Extensions["order"] = "first";
        }
    }

    private sealed class OverwriteMember : IProblemContributor
    {
        public void Contribute(ProblemContext context) => context.Problem.Extensions["order"] = "second";
    }
}
