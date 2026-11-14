using System.Net;
using System.Text;
using System.Text.Json;

namespace Sondor.Http.Tests;

/// <summary>The hook for error formats the package does not know.</summary>
[TestFixture]
public sealed class ErrorReaderTests
{
    private const string Envelope = """{"success":false,"errors":[{"code":"E42","message":"Invalid token"}]}""";

    [Test]
    public async Task ErrorReaders_Should_TurnAnotherFormatIntoAnError()
    {
        var client = Clients.Create(
            () => Responses.Json(HttpStatusCode.BadRequest, Envelope),
            options => options.ErrorReaders.Add(new EnvelopeReader()));

        var error = (await client.GetAsync<Order>("a")).Error;
        var failure = error.GetHttpFailure()!;

        Assert.That(error.Code, Is.EqualTo("Gateway.Rejected"));
        Assert.That(error.Type, Is.EqualTo(ErrorType.Validation));
        Assert.That(failure.Status, Is.EqualTo(400));
        Assert.That(failure.Problem!.ErrorCode, Is.EqualTo("E42"));
    }

    [Test]
    public async Task ErrorReaders_Should_FallThroughToTheProblemReader_When_NoneUnderstandsTheResponse()
    {
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.NotFound, """{"errorCode":"Order.NotFound"}"""),
            options => options.ErrorReaders.Add(new EnvelopeReader()));

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Order.NotFound"));
    }

    [Test]
    public async Task ErrorReaders_Should_AskTheReadersInOrder_AndStopAtTheFirstAnswer()
    {
        var order = new List<string>();
        var client = Clients.Create(
            () => Responses.Empty(HttpStatusCode.Conflict),
            options =>
            {
                options.ErrorReaders.Add(new DelegateReader("first", order, null));
                options.ErrorReaders.Add(new DelegateReader("second", order, new Error("Second.Code", "second", ErrorType.Conflict)));
                options.ErrorReaders.Add(new DelegateReader("third", order, new Error("Third.Code", "third", ErrorType.Conflict)));
            });

        var error = (await client.GetAsync<Order>("a")).Error;

        Assert.That(error.Code, Is.EqualTo("Second.Code"));
        Assert.That(string.Join(",", order), Is.EqualTo("first,second"));
    }

    [Test]
    public async Task ErrorReaders_Should_ShareTheBodyThatIsReadOnce()
    {
        var reads = 0;
        var seen = new List<string>();
        var client = Clients.Create(
            () => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new CountingContent("""{"a":1}""", () => reads++) },
            options =>
            {
                options.ErrorReaders.Add(new InspectingReader(async (context, ct) =>
                {
                    seen.Add(Encoding.UTF8.GetString((await context.ReadBodyAsync(ct)).Span));
                    seen.Add((await context.ReadJsonAsync(ct))?.RootElement.GetRawText() ?? "none");
                }));
                options.ErrorReaders.Add(new InspectingReader(async (context, ct) =>
                {
                    seen.Add(Encoding.UTF8.GetString((await context.ReadBodyAsync(ct)).Span));
                    seen.Add((await context.ReadJsonAsync(ct))?.RootElement.GetRawText() ?? "none");
                }));
            });

        await client.GetAsync<Order>("a");

        Assert.That(reads, Is.EqualTo(1));
        Assert.That(seen, Has.Count.EqualTo(4));
        Assert.That(seen, Is.All.EqualTo("""{"a":1}"""));
    }

    [Test]
    public async Task HttpErrorContext_Should_TellTheReader_What_TheResponseIs()
    {
        HttpErrorContext? captured = null;
        string? contentType = null;
        string? retry = null;
        var client = Clients.Create(
            () =>
            {
                var response = Responses.Json(HttpStatusCode.Conflict, "plain text", "text/plain");
                response.Headers.Add("X-Request-Id", "abc");

                return response;
            },
            options => options.ErrorReaders.Add(new InspectingReader((context, ct) =>
            {
                captured = context;
                contentType = context.ContentHeaders.ContentType?.MediaType;
                retry = context.Headers.GetValues("X-Request-Id").Single();

                return ValueTask.CompletedTask;
            })));

        await client.GetAsync<Order>("a");

        Assert.That(captured!.Status, Is.EqualTo(409));
        Assert.That(captured.Method, Is.EqualTo("GET"));
        Assert.That(captured.Host, Is.EqualTo("orders.example.com"));
        Assert.That(captured.Options, Is.Not.Null);
        Assert.That(contentType, Is.EqualTo("text/plain"));
        Assert.That(retry, Is.EqualTo("abc"));
    }

    [Test]
    public async Task HttpErrorContext_Should_NotParseTheBody_When_ItIsNotJsonCutOrBroken()
    {
        var results = new List<JsonDocument?>();
        var bodies = new List<int>();
        var truncated = new List<bool>();

        foreach (var (mediaType, body, limit) in new[]
        {
            ("text/plain", """{"a":1}""", 1024),
            ("application/json", """{"a":1}""", 3),
            ("application/json", "{broken", 1024),
            ("application/json", "", 1024),
            ("application/problem+json", new string('a', 5000), 6000)
        })
        {
            var client = Clients.Create(
                () => Responses.Json(HttpStatusCode.BadRequest, body, mediaType),
                options =>
                {
                    options.MaxErrorBodyBytes = limit;
                    options.ErrorReaders.Add(new InspectingReader(async (context, ct) =>
                    {
                        results.Add(await context.ReadJsonAsync(ct));
                        bodies.Add((await context.ReadBodyAsync(ct)).Length);
                        truncated.Add(context.BodyTruncated);
                    }));
                });

            await client.GetAsync<Order>("a");
        }

        Assert.That(results, Is.All.Null);
        Assert.That(string.Join(",", bodies), Is.EqualTo("7,3,7,0,5000"));
        Assert.That(string.Join(",", truncated), Is.EqualTo("False,True,False,False,False"));
    }

    [Test]
    public async Task HttpErrorContext_Should_ReadABodyOfUnknownLength_UpToTheLimit()
    {
        var sizes = new List<(int Length, bool Truncated)>();

        foreach (var (length, limit) in new[] { (10_000L, 20_000), (10_000L, 10_000), (10_000L, 9_999), (10_000L, 5_000), (100L, 4096) })
        {
            var client = Clients.Create(
                () => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = EndlessContent.Of(length, "text/plain") },
                options =>
                {
                    options.MaxErrorBodyBytes = limit;
                    options.ErrorReaders.Add(new InspectingReader(async (context, ct) =>
                        sizes.Add(((await context.ReadBodyAsync(ct)).Length, context.BodyTruncated))));
                });

            await client.GetAsync<Order>("a");
        }

        Assert.That(string.Join(" ", sizes), Is.EqualTo("(10000, False) (10000, False) (9999, True) (5000, True) (100, False)"));
    }

    [Test]
    public async Task HttpErrorContext_Should_MakeErrorsThatCarryTheFailure()
    {
        Error created = default;
        Error fromStatus = default;
        var client = Clients.Create(
            () => Responses.Empty(HttpStatusCode.TooManyRequests),
            options => options.ErrorReaders.Add(new InspectingReader((context, ct) =>
            {
                created = context.CreateError("Gateway.Busy", "A {brace} stays literal.", ErrorType.RateLimited, new RemoteProblem(title: "Busy"), "trace-1");
                fromStatus = context.CreateStatusError();

                return ValueTask.CompletedTask;
            })));

        await client.GetAsync<Order>("a");

        Assert.That(created.Code, Is.EqualTo("Gateway.Busy"));
        Assert.That(created.Description, Is.EqualTo("A {brace} stays literal."));
        Assert.That(created.GetHttpFailure()!.TraceId, Is.EqualTo("trace-1"));
        Assert.That(created.GetHttpFailure()!.Problem!.Title, Is.EqualTo("Busy"));
        Assert.That(fromStatus.Code, Is.EqualTo("Request.RateLimited"));
        Assert.That(fromStatus.Type, Is.EqualTo(ErrorType.RateLimited));
    }

    [Test]
    public async Task ErrorReaders_Should_PropagateAnExceptionOfTheReader_BecauseItIsABug()
    {
        var client = Clients.Create(
            () => Responses.Empty(HttpStatusCode.Conflict),
            options => options.ErrorReaders.Add(new InspectingReader((_, _) => throw new InvalidOperationException("bug"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync<Order>("a"));
    }

    private sealed class EnvelopeReader : IHttpErrorReader
    {
        public async ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken)
        {
            var document = await context.ReadJsonAsync(cancellationToken);

            if (document?.RootElement.TryGetProperty("success", out var success) != true || success.GetBoolean())
            {
                return null;
            }

            var code = document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString();

            return context.CreateError("Gateway.Rejected", "The gateway refused the request.", ErrorType.Validation, new RemoteProblem(errorCode: code));
        }
    }

    private sealed class DelegateReader(string name, List<string> order, Error? answer) : IHttpErrorReader
    {
        public ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken)
        {
            order.Add(name);

            return ValueTask.FromResult(answer);
        }
    }

    private sealed class InspectingReader(Func<HttpErrorContext, CancellationToken, ValueTask> inspect) : IHttpErrorReader
    {
        public async ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken)
        {
            await inspect(context, cancellationToken);

            return null;
        }
    }

    private sealed class CountingContent : HttpContent
    {
        private readonly byte[] _bytes;
        private readonly Action _onRead;

        public CountingContent(string text, Action onRead)
        {
            _bytes = Encoding.UTF8.GetBytes(text);
            _onRead = onRead;
            Headers.ContentType = new("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _onRead();

            return stream.WriteAsync(_bytes).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;

            return true;
        }
    }
}
