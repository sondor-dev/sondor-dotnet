using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.AspNetCore.Benchmarks;

/// <summary>
/// Writing a response for a result and for an exception, each against the framework doing the same by hand: a success must cost what
/// the framework's own result costs, and a failure is the problem written through the framework's problem details service. The
/// response goes to <see cref="Stream.Null"/>, so what is measured is the work and not a socket.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ResultAndExceptionBenchmarks
{
    private static readonly Order Value = new(12, "Pending");

    private readonly Error _notFound = Error.NotFound("Order", "Id");
    private readonly InvalidOperationException _unexpected = new("a connection string that must not leak");
    private readonly PaymentDeclinedException _declined = new("The card was declined.");
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private DefaultHttpContext _context = null!;
    private IExceptionHandler _sondorHandler = null!;
    private IExceptionHandler _frameworkHandler = null!;
    private ServiceProvider _frameworkProvider = null!;
    private IServiceScope _frameworkScope = null!;
    private DefaultHttpContext _frameworkContext = null!;

    /// <summary>Builds the package's container and a framework-only one.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSondorProblemDetails(options =>
            options.Exceptions.Map<PaymentDeclinedException>(e => new Error("Payment.Declined", e.Message, ErrorType.Conflict)));

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _context = Context(_scope);
        _sondorHandler = _provider.GetServices<IExceptionHandler>().Single();

        var framework = new ServiceCollection();

        framework.AddLogging();
        framework.AddProblemDetails();
        framework.AddSingleton<IExceptionHandler, FrameworkOnlyHandler>();

        _frameworkProvider = framework.BuildServiceProvider();
        _frameworkScope = _frameworkProvider.CreateScope();
        _frameworkContext = Context(_frameworkScope);
        _frameworkHandler = _frameworkProvider.GetServices<IExceptionHandler>().Single();
    }

    /// <summary>Disposes the containers.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
        _frameworkScope.Dispose();
        _frameworkProvider.Dispose();
    }

    /// <summary>
    /// The framework's <c>200 OK</c> with a value. Every result is executed through <see cref="IResult"/>, as the framework does for an
    /// endpoint's return value; executing the concrete type directly skips an interface call and allocates less, which would blame
    /// this package for a difference that is not its own.
    /// </summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Success with a value")]
    public Task SuccessValueFramework() => Execute(TypedResults.Ok(Value), _frameworkContext);

    /// <summary><c>ToHttpResult()</c> on a success with a value.</summary>
    [Benchmark, BenchmarkCategory("Success with a value")]
    public Task SuccessValueSondor() => Execute(Result.Success(Value).ToHttpResult(), _context);

    /// <summary>The framework's <c>204 No Content</c>.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Success without a value")]
    public Task SuccessEmptyFramework() => Execute(TypedResults.NoContent(), _frameworkContext);

    /// <summary><c>ToHttpResult()</c> on a success without a value.</summary>
    [Benchmark, BenchmarkCategory("Success without a value")]
    public Task SuccessEmptySondor() => Execute(Result.Success().ToHttpResult(), _context);

    /// <summary>A problem written by hand and sent through the framework's problem result.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Failure")]
    public Task FailureFramework() =>
        Execute(
            TypedResults.Problem(new ProblemDetails { Status = 404, Title = "Not Found", Detail = _notFound.Description, Instance = "/orders/12" }),
            _frameworkContext);

    /// <summary><c>ToHttpResult()</c> on a failed result.</summary>
    [Benchmark, BenchmarkCategory("Failure")]
    public Task FailureSondor() => Execute(Result.Failure(_notFound).ToHttpResult(), _context);

    /// <summary>An exception answered by a framework <c>IExceptionHandler</c> that writes a generic 500 problem.</summary>
    [Benchmark(Baseline = true), BenchmarkCategory("Unexpected exception")]
    public ValueTask<bool> UnexpectedFramework() => _frameworkHandler.TryHandleAsync(_frameworkContext, _unexpected, CancellationToken.None);

    /// <summary>An unexpected exception answered by this package: the generic 500.</summary>
    [Benchmark, BenchmarkCategory("Unexpected exception")]
    public ValueTask<bool> UnexpectedSondor() => _sondorHandler.TryHandleAsync(_context, _unexpected, CancellationToken.None);

    /// <summary>An exception the application mapped to an error.</summary>
    [Benchmark, BenchmarkCategory("Unexpected exception")]
    public ValueTask<bool> MappedSondor() => _sondorHandler.TryHandleAsync(_context, _declined, CancellationToken.None);

    // Not inlined, so the JIT cannot see that the result never escapes and place it on the stack. It does that for the framework's own
    // result when everything inlines, which would make the framework look cheaper than it is in a real request, where the result is
    // returned from an endpoint and executed by the framework.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task Execute(IResult result, HttpContext context) => result.ExecuteAsync(context);

    private static DefaultHttpContext Context(IServiceScope scope)
    {
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };

        context.Request.Path = "/orders/12";
        context.Response.Body = Stream.Null;

        return context;
    }

    private sealed record Order(int Id, string Status);

    private sealed class PaymentDeclinedException(string message) : Exception(message);

    /// <summary>What a service writes without this package: a generic 500 through the framework's problem details service.</summary>
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
