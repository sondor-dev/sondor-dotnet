using Microsoft.Extensions.Logging;

namespace Sondor.Http.Internal;

/// <summary>
/// The only things the package logs. A line never holds a path, a query string, a header or a body: only the method, the host,
/// the status, the elapsed time and the error code.
/// </summary>
internal static partial class HttpLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "{Method} {Host} answered {Status} in {ElapsedMs} ms")]
    public static partial void Succeeded(ILogger logger, string method, string host, int status, double elapsedMs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Method} {Host} was refused with {Status} ({Code}) in {ElapsedMs} ms")]
    public static partial void Refused(ILogger logger, string method, string host, int status, string code, double elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "{Method} {Host} failed with {Status} ({Code}) in {ElapsedMs} ms")]
    public static partial void FailedWithStatus(ILogger logger, string method, string host, int status, string code, double elapsedMs);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "{Method} {Host} failed without a usable response ({Code}) in {ElapsedMs} ms")]
    public static partial void FailedWithoutStatus(ILogger logger, string method, string host, string code, double elapsedMs);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "No token was added to a request to {Host}: {Reason}")]
    public static partial void TokenNotAdded(ILogger logger, string host, string reason);
}
