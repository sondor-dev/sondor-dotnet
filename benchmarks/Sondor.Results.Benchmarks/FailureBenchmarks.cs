using BenchmarkDotNet.Attributes;

namespace Sondor.Results.Benchmarks;

/// <summary>
/// The reason the package exists: an expected failure returned as a value, against the same failure thrown and caught. Throwing
/// is slow because the runtime captures a stack trace, so a failure that happens often (a lookup that finds nothing, a
/// validation that refuses) should not be an exception.
/// </summary>
[MemoryDiagnoser]
public class FailureBenchmarks
{
    private static readonly Error NotFound = new("Node.NotFound", "There is no such node.", ErrorType.NotFound);

    private int _id = 7;

    [Benchmark(Baseline = true)]
    public int FailureAsValue()
    {
        var result = Find(_id);

        return result.IsFailure ? -1 : result.Value;
    }

    [Benchmark]
    public int FailureAsException()
    {
        try
        {
            return FindOrThrow(_id);
        }
        catch (KeyNotFoundException)
        {
            return -1;
        }
    }

    private static Result<int> Find(int id) => id > 0 ? NotFound : Result.Success(id);

    private static int FindOrThrow(int id) => id > 0 ? throw new KeyNotFoundException(NotFound.Code) : id;
}
