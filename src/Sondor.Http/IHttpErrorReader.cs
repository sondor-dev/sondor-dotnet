namespace Sondor.Http;

/// <summary>
/// Reads a failed response in a format the package does not know, such as an API's own error envelope, and turns it into an
/// <see cref="Error"/>. Register one with <c>AddErrorReader</c> or <see cref="HttpResultOptions.ErrorReaders"/>.
/// </summary>
/// <remarks>
/// Readers are asked in order for every response that is not a success. The first to return an error wins; when every reader
/// returns null the package reads the RFC 9457 problem document, and then falls back to the status alone. A reader is shared by every
/// call of its client, so it must be safe to use from several threads, and it should return null for a response that is not its
/// format. The body is read once and shared, so reading it is cheap. What a reader puts in the error is its own responsibility: a
/// body is the other side's text, so build the <see cref="Error.Code"/> from a fixed set or check it, and treat a description with the
/// care <see cref="HttpResultOptions.UseRemoteDetail"/> describes.
/// </remarks>
public interface IHttpErrorReader
{
    /// <summary>Reads the failed response.</summary>
    /// <param name="context">The response: status, headers and a bounded body.</param>
    /// <param name="cancellationToken">Cancelled when the call times out or the caller cancels.</param>
    /// <returns>The error, or null when this is not a response the reader understands.</returns>
    ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken);
}
