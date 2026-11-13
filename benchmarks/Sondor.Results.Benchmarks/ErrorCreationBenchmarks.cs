using BenchmarkDotNet.Attributes;

namespace Sondor.Results.Benchmarks;

/// <summary>
/// What it costs to make an error, which happens on the failure path only: the constructor as it always was, a call to a
/// built-in factory, and a template declared by the application. The success path is measured by the other classes, where the
/// larger <see cref="Error"/> shows up as the size of every result.
/// </summary>
[MemoryDiagnoser]
public class ErrorCreationBenchmarks
{
    private static readonly ErrorTemplate Overdrawn = new(
        "Account.Overdrawn", ErrorType.Conflict, "The {Account} cannot go below the {Limit}.");

    private static readonly ErrorTemplate OrderNotFound = ErrorTemplates.NotFound.WithCode("Order.NotFound");

    private static readonly ErrorTemplate Busy = new("Export.Busy", ErrorType.Conflict, "An export is already running.");

    private string _entity = "Order";
    private string _property = "Id";
    private string _description = "No order was found.";

    /// <summary>A fixed error made with the constructor: the floor, and what every error cost before templates.</summary>
    [Benchmark(Baseline = true)]
    public Error Constructor() => new("Order.NotFound", _description, ErrorType.NotFound);

    /// <summary>A built-in factory: two terms, a formatted description and the arguments object.</summary>
    [Benchmark]
    public Error Factory() => Error.NotFound(_entity, _property);

    /// <summary>The factory with the looked-up value attached as data.</summary>
    [Benchmark]
    public Error FactoryWithData() => Error.NotFound(_entity, _property, 42);

    /// <summary>A template with a code of its own, made from a built-in one.</summary>
    [Benchmark]
    public Error DerivedTemplate() =>
        OrderNotFound.Create(ErrorArgument.Term(ErrorArgumentNames.Entity, _entity), ErrorArgument.Term(ErrorArgumentNames.Property, _property));

    /// <summary>A template declared by the application.</summary>
    [Benchmark]
    public Error CustomTemplate() =>
        Overdrawn.Create(ErrorArgument.Term("Account", _entity), ErrorArgument.Term("Limit", _property));

    /// <summary>A template with no placeholders: the sentence is reused, so only the error itself is made.</summary>
    [Benchmark]
    public static Error TemplateWithoutPlaceholders() => Busy.Create();

    /// <summary>The error turned into a failed result, as a handler returns it.</summary>
    [Benchmark]
    public Result<int> FactoryAsFailedResult() => Error.NotFound(_entity, _property);
}
