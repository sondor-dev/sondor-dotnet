using BenchmarkDotNet.Attributes;

namespace Sondor.Results.Benchmarks;

/// <summary>
/// What it costs to chain steps. The steps are static lambdas, which the compiler caches, so what shows up is the cost of the
/// methods; a lambda that captures a variable allocates in the caller, which is measured separately below.
/// </summary>
[MemoryDiagnoser]
public class ChainingBenchmarks
{
    private static readonly Error TooSmall = new("Number.TooSmall", "The number is too small.", ErrorType.Validation);

    private int _value = 41;
    private int _limit = 40;

    /// <summary>The same four steps with no result: the floor.</summary>
    [Benchmark(Baseline = true)]
    public int PlainCode()
    {
        var value = _value;

        if (value <= 0)
        {
            return 0;
        }

        value += 1;
        value *= 2;

        return value;
    }

    /// <summary>Ensure, Map, Bind and Tap on a success: every step runs.</summary>
    [Benchmark]
    public Result<int> ChainOnSuccess() =>
        Result.Success(_value)
            .Ensure(static value => value > 0, TooSmall)
            .Map(static value => value + 1)
            .Bind(static value => Result.Success(value * 2))
            .Tap(static _ => { });

    /// <summary>The first step fails: the rest are skipped.</summary>
    [Benchmark]
    public Result<int> ChainOnFailure() =>
        Result.Success(-_value)
            .Ensure(static value => value > 0, TooSmall)
            .Map(static value => value + 1)
            .Bind(static value => Result.Success(value * 2))
            .Tap(static _ => { });

    /// <summary>The same chain, but a step captures a local: the compiler allocates a closure and a delegate for it.</summary>
    [Benchmark]
    public Result<int> ChainOnSuccessWithACapturingLambda()
    {
        var limit = _limit;

        return Result.Success(_value)
            .Ensure(value => value > limit, TooSmall)
            .Map(static value => value + 1)
            .Bind(static value => Result.Success(value * 2))
            .Tap(static _ => { });
    }

    [Benchmark]
    public string Match() =>
        Result.Success(_value).Match(static value => "ok", static error => error.Code);
}
