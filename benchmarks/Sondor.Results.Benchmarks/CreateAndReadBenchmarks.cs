using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;

namespace Sondor.Results.Benchmarks;

/// <summary>
/// What it costs to make a result and read it, against returning the bare value. A result is a struct, so none of this should
/// allocate; the allocation column says whether that holds.
/// </summary>
[MemoryDiagnoser]
public class CreateAndReadBenchmarks
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);

    private int _value = 41;

    /// <summary>The floor: no result at all.</summary>
    [Benchmark(Baseline = true)]
    public int BareValue() => _value + 1;

    [Benchmark]
    public int SuccessAndRead()
    {
        var result = Result.Success(_value);

        return result.IsSuccess ? result.Value + 1 : 0;
    }

    [Benchmark]
    public int SuccessThroughImplicitConversion()
    {
        Result<int> result = _value;

        return result.IsSuccess ? result.Value + 1 : 0;
    }

    [Benchmark]
    public int FailureAndRead()
    {
        var result = Result.Failure<int>(NotFound);

        return result.IsFailure ? result.Error.Code.Length + _value : 0;
    }

    [Benchmark]
    public int TryGetValue() => Result.Success(_value).TryGetValue(out var value) ? value + 1 : 0;

    /// <summary>Reading through the interface by a constrained generic call, the way a pipeline behaviour can: no boxing.</summary>
    [Benchmark]
    public bool ReadThroughConstrainedGeneric() => IsSuccess(Result.Success(_value));

    /// <summary>
    /// Reading through a plain interface variable boxes the struct in principle. .NET 10 can put a box that does not escape on the
    /// stack, as here, so it shows no allocation; a box that escapes (stored, or passed to code the JIT cannot see through) does
    /// allocate, which is why a pipeline that reads results should use the constrained generic call.
    /// </summary>
    [Benchmark]
    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "The interface variable is the point: it boxes the struct, and the benchmark measures what that costs when the box does not escape.")]
    public bool ReadThroughBoxedInterface()
    {
        IResultState state = Result.Success(_value);

        return state.IsSuccess;
    }

    private static bool IsSuccess<TState>(TState state)
        where TState : IResultState => state.IsSuccess;
}
