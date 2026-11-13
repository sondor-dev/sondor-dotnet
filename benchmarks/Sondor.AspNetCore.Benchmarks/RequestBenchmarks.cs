using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sondor.AspNetCore.Benchmarks;

/// <summary>
/// Whole requests through an in-memory server, each against an application that does the same with the framework alone (the
/// problem details service, an exception handler and the status code pages written by hand): a success, an unknown URL, a failed
/// result and an exception. Every case runs after 0 and after 10,000 earlier requests: a cost that grows with the requests already
/// served would show as a difference between the two.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class RequestBenchmarks
{
    private WebApplication _sondor = null!;
    private WebApplication _framework = null!;
    private HttpClient _sondorClient = null!;
    private HttpClient _frameworkClient = null!;

    /// <summary>How many requests of each kind are sent before measuring.</summary>
    [Params(0, 10_000)]
    public int Earlier { get; set; }

    /// <summary>Starts both applications and sends the earlier requests.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _sondor = Start(useSondor: true);
        _framework = Start(useSondor: false);

        await _sondor.StartAsync();
        await _framework.StartAsync();

        _sondorClient = _sondor.GetTestClient();
        _frameworkClient = _framework.GetTestClient();

        for (var request = 0; request < Earlier; request++)
        {
            foreach (var path in new[] { "/ok", "/nothing", "/orders/12", "/boom" })
            {
                await Send(_sondorClient, path);
                await Send(_frameworkClient, path);
            }
        }
    }

    /// <summary>Stops both applications.</summary>
    [GlobalCleanup]
    public async Task Cleanup()
    {
        _sondorClient.Dispose();
        _frameworkClient.Dispose();
        await _sondor.StopAsync();
        await _framework.StopAsync();
        await _sondor.DisposeAsync();
        await _framework.DisposeAsync();
    }

    /// <summary>A success, with the framework alone.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Success")]
    public Task SuccessFramework() => Send(_frameworkClient, "/ok");

    /// <summary>A success through <c>ToHttpResult()</c>.</summary>
    [Benchmark, BenchmarkCategory("Success")]
    public Task SuccessSondor() => Send(_sondorClient, "/ok");

    /// <summary>An unknown URL, with the framework alone.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Unknown URL")]
    public Task UnknownUrlFramework() => Send(_frameworkClient, "/nothing");

    /// <summary>An unknown URL, with this package.</summary>
    [Benchmark, BenchmarkCategory("Unknown URL")]
    public Task UnknownUrlSondor() => Send(_sondorClient, "/nothing");

    /// <summary>A failed result, with the framework alone.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Failed result")]
    public Task FailureFramework() => Send(_frameworkClient, "/orders/12");

    /// <summary>A failed result through <c>ToHttpResult()</c>.</summary>
    [Benchmark, BenchmarkCategory("Failed result")]
    public Task FailureSondor() => Send(_sondorClient, "/orders/12");

    /// <summary>An exception, with the framework alone.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Exception")]
    public Task ExceptionFramework() => Send(_frameworkClient, "/boom");

    /// <summary>An exception, with this package.</summary>
    [Benchmark, BenchmarkCategory("Exception")]
    public Task ExceptionSondor() => Send(_sondorClient, "/boom");

    private static async Task Send(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);

        await response.Content.ReadAsByteArrayAsync();
    }

    private static WebApplication Start(bool useSondor)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var error = Error.NotFound("Order", "Id");

        if (useSondor)
        {
            builder.Services.AddSondorProblemDetails();
        }
        else
        {
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<FrameworkOnlyHandler>();
        }

        var app = builder.Build();

        if (useSondor)
        {
            app.UseSondorProblemDetails();
            app.MapGet("/ok", () => Result.Success(new Order(12, "Pending")).ToHttpResult());
            app.MapGet("/orders/{id}", (int id) => Result.Failure(error).ToHttpResult());
        }
        else
        {
            app.UseExceptionHandler();
            app.UseStatusCodePages();
            app.MapGet("/ok", () => TypedResults.Ok(new Order(12, "Pending")));
            app.MapGet("/orders/{id}", (int id) =>
                TypedResults.Problem(new ProblemDetails { Status = 404, Title = "Not Found", Detail = error.Description }));
        }

        app.MapGet("/boom", () => Throw());

        return app;
    }

    private static IResult Throw() => throw new InvalidOperationException("a connection string that must not leak");

    private sealed record Order(int Id, string Status);

    private sealed class FrameworkOnlyHandler(IProblemDetailsService problems) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            httpContext.Response.StatusCode = 500;

            return await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails { Status = 500, Title = "Internal Server Error" },
            });
        }
    }
}
