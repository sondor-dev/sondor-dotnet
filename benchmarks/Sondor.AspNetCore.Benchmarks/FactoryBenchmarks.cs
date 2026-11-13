using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Sondor.AspNetCore.Benchmarks;

/// <summary>
/// Turning an <see cref="Error"/> into a problem, which happens on the failure path only: with the defaults, with a documentation
/// link built from the code, and with a text provider and a contributor registered. The floor is the same problem written by hand.
/// </summary>
[MemoryDiagnoser]
public class FactoryBenchmarks
{
    private readonly Error _notFound = Error.NotFound("Order", "Id");
    private ServiceProvider _plain = null!;
    private ServiceProvider _linked = null!;
    private ServiceProvider _extended = null!;
    private IServiceScope _plainScope = null!;
    private IServiceScope _linkedScope = null!;
    private IServiceScope _extendedScope = null!;
    private IErrorProblemFactory _plainFactory = null!;
    private IErrorProblemFactory _linkedFactory = null!;
    private IErrorProblemFactory _extendedFactory = null!;
    private DefaultHttpContext _context = null!;

    /// <summary>Builds three containers and a request.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _plain = Build(_ => { }, _ => { });
        _linked = Build(options => options.TypeUriTemplate = "https://docs.example.com/errors/{code}", _ => { });
        _extended = Build(
            _ => { },
            services =>
            {
                services.AddSingleton<IProblemTextProvider, FixedText>();
                services.AddSingleton<IProblemContributor, OneMember>();
            });

        _plainScope = _plain.CreateScope();
        _linkedScope = _linked.CreateScope();
        _extendedScope = _extended.CreateScope();
        _plainFactory = _plainScope.ServiceProvider.GetRequiredService<IErrorProblemFactory>();
        _linkedFactory = _linkedScope.ServiceProvider.GetRequiredService<IErrorProblemFactory>();
        _extendedFactory = _extendedScope.ServiceProvider.GetRequiredService<IErrorProblemFactory>();

        _context = new DefaultHttpContext();
        _context.Request.Path = "/orders/12";
    }

    /// <summary>Disposes the containers.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _plainScope.Dispose();
        _linkedScope.Dispose();
        _extendedScope.Dispose();
        _plain.Dispose();
        _linked.Dispose();
        _extended.Dispose();
    }

    /// <summary>The floor: the same problem, written by hand.</summary>
    [Benchmark(Baseline = true)]
    public ProblemDetails Handwritten()
    {
        var problem = new ProblemDetails
        {
            Status = 404,
            Title = "Not Found",
            Detail = _notFound.Description,
            Instance = _context.Request.Path.Value,
        };

        problem.Extensions["errorCode"] = _notFound.Code;

        return problem;
    }

    /// <summary>The defaults.</summary>
    [Benchmark]
    public ProblemDetails Default() => _plainFactory.Create(_notFound, _context);

    /// <summary>With a documentation link built from the code.</summary>
    [Benchmark]
    public ProblemDetails WithDocumentationLink() => _linkedFactory.Create(_notFound, _context);

    /// <summary>With a text provider and one contributor registered.</summary>
    [Benchmark]
    public ProblemDetails WithTextProviderAndContributor() => _extendedFactory.Create(_notFound, _context);

    private static ServiceProvider Build(Action<ErrorResponseOptions> configure, Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSondorProblemDetails(configure);
        register(services);

        return services.BuildServiceProvider();
    }

    private sealed class FixedText : IProblemTextProvider
    {
        private static readonly ProblemText Text = new("Introuvable", "Aucune commande n'a ete trouvee.");

        public ProblemText? GetText(Error failure, HttpContext context) => Text;
    }

    private sealed class OneMember : IProblemContributor
    {
        public void Contribute(ProblemContext context) => context.Problem.Extensions["hint"] = "check the identifier";
    }
}
