using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sondor.AspNetCore.Internal;

namespace Sondor.AspNetCore.Tests;

[TestFixture]
public sealed class OptionsAndClassifierTests
{
    [Test]
    public async Task Start_Should_Fail_When_TheTypeTemplateHasNoCodePlaceholder()
    {
        var thrown = await StartFailingAsync(options => options.TypeUriTemplate = "https://docs.example.com/errors");

        Assert.That(thrown.Message, Does.Contain("{code}").And.Contain("https://docs.example.com/errors"));
    }

    [TestCase(200)]
    [TestCase(399)]
    [TestCase(600)]
    public async Task Start_Should_Fail_When_AStatusIsNotAnErrorStatus(int status)
    {
        var byType = await StartFailingAsync(options => options.StatusByType[ErrorType.NotFound] = status);
        var byCode = await StartFailingAsync(options => options.StatusByCode["Order.Late"] = status);
        var cancelled = await StartFailingAsync(options => options.ClientClosedRequestStatus = status);

        Assert.That(byType.Message, Does.Contain("StatusByType[NotFound]").And.Contain(status.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(byCode.Message, Does.Contain("StatusByCode['Order.Late']"));
        Assert.That(cancelled.Message, Does.Contain("ClientClosedRequestStatus"));
    }

    [Test]
    public async Task Start_Should_Fail_When_TheCodeMemberIsBlank()
    {
        var thrown = await StartFailingAsync(options => options.ErrorCodeMember = " ");

        Assert.That(thrown.Message, Does.Contain("ErrorCodeMember"));
    }

    [Test]
    public async Task Start_Should_Succeed_WithTheDefaults()
    {
        await using var app = await TestApp.StartAsync();

        Assert.That(app.App.Services.GetRequiredService<IOptions<ErrorResponseOptions>>().Value.StatusByType[ErrorType.NotFound], Is.EqualTo(404));
    }

    [Test]
    public void AddSondorProblemDetails_Should_RegisterOneOfEverything_AndKeepEveryConfiguration()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSondorProblemDetails(options => options.ErrorCodeMember = "code");
        services.AddSondorProblemDetails(options => options.TagActivity = false);

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetServices<IExceptionHandler>().Count(), Is.EqualTo(1));
        Assert.That(provider.GetServices<IValidateOptions<ErrorResponseOptions>>().Count(), Is.EqualTo(1));

        var options = provider.GetRequiredService<IOptions<ErrorResponseOptions>>().Value;
        Assert.That(options.ErrorCodeMember, Is.EqualTo("code"));
        Assert.That(options.TagActivity, Is.False);
    }

    [Test]
    public void AddSondorProblemDetails_Should_RejectMissingServices()
    {
        Assert.That(() => ErrorResponseExtensions.AddSondorProblemDetails(null!), Throws.ArgumentNullException);
        Assert.That(() => ErrorResponseExtensions.UseSondorProblemDetails(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Options_Should_BindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Errors:TypeUriTemplate"] = "https://docs.example.com/errors/{code}",
                ["Errors:IncludeExceptionDetails"] = "true",
                ["Errors:StatusByType:Validation"] = "422",
                ["Errors:StatusByCode:Order.Late"] = "410",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSondorProblemDetails();
        services.Configure<ErrorResponseOptions>(configuration.GetSection("Errors"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ErrorResponseOptions>>().Value;

        Assert.That(options.TypeUriTemplate, Is.EqualTo("https://docs.example.com/errors/{code}"));
        Assert.That(options.IncludeExceptionDetails, Is.True);
        Assert.That(options.StatusByType[ErrorType.Validation], Is.EqualTo(422));
        Assert.That(options.StatusByType[ErrorType.NotFound], Is.EqualTo(404), "bound values are added to the defaults");
        Assert.That(options.StatusByCode["Order.Late"], Is.EqualTo(410));
    }

    [Test]
    public async Task UseSondorProblemDetails_Should_AddNothing_When_BothPiecesAreOff()
    {
        await using var app = await TestApp.StartAsync(
            options =>
            {
                options.HandleExceptions = false;
                options.WriteStatusCodeBodies = false;
            });

        var response = await app.Client.GetAsync("/nothing-here");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Empty);
    }

    // Which requests get a problem response.

    [Test]
    public void IsApiRequest_Should_BeTrue_ForAnApiControllerAndAMinimalApi()
    {
        Assert.That(RequestClassifier.IsApiRequest(WithEndpoint(new ApiMarker())), Is.True);
        Assert.That(RequestClassifier.IsApiRequest(WithEndpoint()), Is.True);
    }

    [Test]
    public void IsApiRequest_Should_BeFalse_ForAViewControllerAndARazorPage()
    {
        Assert.That(RequestClassifier.IsApiRequest(WithEndpoint(new ControllerActionDescriptor())), Is.False);
        Assert.That(RequestClassifier.IsApiRequest(WithEndpoint(new PageActionDescriptor())), Is.False);
    }

    [Test]
    public void IsApiRequest_Should_PreferTheEndpointTheExceptionHandlerKept()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IExceptionHandlerFeature>(new KeptEndpoint(Endpoint(new ControllerActionDescriptor())));

        Assert.That(RequestClassifier.IsApiRequest(context), Is.False);
    }

    [TestCase(null, true)]
    [TestCase("*/*", true)]
    [TestCase("application/json", true)]
    [TestCase("application/problem+json, */*;q=0.1", true)]
    [TestCase("text/html", false)]
    [TestCase("application/xhtml+xml", false)]
    [TestCase("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", false)]
    [TestCase("application/json;q=0.5, text/html", false)]
    [TestCase("text/html;q=0.5, application/json", true)]
    [TestCase("text/html;q=0.9, application/json;q=0.8", false)]
    [TestCase("text/html;q=0, application/json", true)]
    [TestCase("TEXT/HTML", false)]
    [TestCase("text/html;level=1;q=0.5, application/json;q=0.4", false)]
    [TestCase("text/html;level=1;Q=0.5, application/json;q=0.6", true)]
    [TestCase("text/html;q=abc, application/json;q=0.5", false)]
    [TestCase("text/html;q=", false)]
    [TestCase(",,text/html", false)]
    [TestCase("  ,  ", true)]
    [TestCase("application/*", true)]
    [TestCase("application/json,text/html", true)]
    [TestCase(" text/html ; q=0.5 , application/json ; q=0.4", false)]
    public void IsApiRequest_Should_ReadTheAcceptHeader_WhenThereIsNoEndpoint(string? accept, bool expected)
    {
        var context = new DefaultHttpContext();

        if (accept is not null)
        {
            context.Request.Headers.Accept = accept;
        }

        Assert.That(RequestClassifier.IsApiRequest(context), Is.EqualTo(expected));
    }

    [Test]
    public void IsApiRequest_Should_ReadEveryAcceptHeaderValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = new Microsoft.Extensions.Primitives.StringValues(["application/json", "text/html;q=0.5"]);

        Assert.That(RequestClassifier.IsApiRequest(context), Is.True);

        context.Request.Headers.Accept = new Microsoft.Extensions.Primitives.StringValues(["application/json;q=0.2", "text/html"]);

        Assert.That(RequestClassifier.IsApiRequest(context), Is.False);
    }

    [Test]
    public void TypeLinks_Should_ExpandTheCode_AndKeepOneInstancePerLink()
    {
        var first = TypeLinks.Expand("https://docs.example.com/errors/{code}", "Order.Late");
        var second = TypeLinks.Expand("https://docs.example.com/errors/{code}", "Order.Late");

        Assert.That(first, Is.EqualTo("https://docs.example.com/errors/Order.Late"));
        Assert.That(second, Is.SameAs(first));
        Assert.That(TypeLinks.Expand("https://docs.example.com/{code}/{code}", "A B"), Is.EqualTo("https://docs.example.com/A%20B/A%20B"));
    }

    [Test]
    public void TypeLinks_Should_StayWithinItsLimit_AndStayCorrect_ForCodesWithoutEnd()
    {
        for (var code = 0; code < 2_000; code++)
        {
            Assert.That(TypeLinks.Expand("https://docs.example.com/limit/{code}", $"Made.Up{code}"), Is.EqualTo($"https://docs.example.com/limit/Made.Up{code}"));
        }

        Assert.That(TypeLinks.Count, Is.LessThanOrEqualTo(512));
    }

    [Test]
    public void Create_Should_RunContributors_That_ComeAsAnyKindOfSequence()
    {
        var contributors = new List<IProblemContributor> { new MarkingContributor() };
        var factory = new ErrorProblemFactory(Options.Create(new ErrorResponseOptions()), contributors);

        var problem = factory.Create(Error.NotFound("Order", "Id"), new DefaultHttpContext());

        Assert.That(problem.Extensions["marked"], Is.EqualTo(true));
    }

    private sealed class MarkingContributor : IProblemContributor
    {
        public void Contribute(ProblemContext context) => context.Problem.Extensions["marked"] = true;
    }

    private static async Task<OptionsValidationException> StartFailingAsync(Action<ErrorResponseOptions> configure)
    {
        try
        {
            await using var app = await TestApp.StartAsync(configure);
        }
        catch (OptionsValidationException exception)
        {
            return exception;
        }

        Assert.Fail("The application started with invalid options.");

        return null!;
    }

    private static DefaultHttpContext WithEndpoint(params object[] metadata)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(Endpoint(metadata));

        return context;
    }

    private static Endpoint Endpoint(params object[] metadata) =>
        new(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "test");

    private sealed class ApiMarker : IApiBehaviorMetadata;

    private sealed class KeptEndpoint(Endpoint endpoint) : IExceptionHandlerFeature
    {
        public Exception Error { get; } = new InvalidOperationException();

        public string Path { get; } = "/";

        public Endpoint Endpoint { get; } = endpoint;

        public RouteValueDictionary? RouteValues { get; }
    }
}
