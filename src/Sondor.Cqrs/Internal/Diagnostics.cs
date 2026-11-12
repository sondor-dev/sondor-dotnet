using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sondor.Cqrs.Internal;

/// <summary>The instruments and tag names. One of each for the whole process: a new one for every request would leak.</summary>
internal static class Diagnostics
{
    public const string HandlerTag = "cqrs.handler";
    public const string RequestTag = "cqrs.request";
    public const string KindTag = "cqrs.request.kind";
    public const string OutcomeTag = "cqrs.outcome";
    public const string ErrorCodeTag = "cqrs.error.code";
    public const string ErrorTypeTag = "error.type";

    public const string Success = "success";
    public const string Failure = "failure";
    public const string Error = "error";
    public const string Cancelled = "cancelled";

    public static readonly ActivitySource Source = new(CqrsTelemetry.SourceName);

    private static readonly Meter MeterInstance = new(CqrsTelemetry.MeterName);

    public static readonly Counter<long> Requests = MeterInstance.CreateCounter<long>(
        "cqrs.requests", "{request}", "Requests handled, by request, kind and outcome.");

    public static readonly Histogram<double> Duration = MeterInstance.CreateHistogram<double>(
        "cqrs.request.duration", "s", "How long requests took to handle, by request, kind and outcome.");
}

/// <summary>Whether a request is a command, a query or neither, worked out once for each request type.</summary>
internal static class RequestKind<TRequest>
{
    public static readonly string Value = Of(typeof(TRequest));

    private static string Of(Type type)
    {
        string? kind = null;

        foreach (var contract in type.GetInterfaces())
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            var definition = contract.GetGenericTypeDefinition();

            if (definition == typeof(ICommand<>))
            {
                return "command";
            }

            if (definition == typeof(IQuery<>))
            {
                kind = "query";
            }
        }

        return kind ?? "request";
    }
}
