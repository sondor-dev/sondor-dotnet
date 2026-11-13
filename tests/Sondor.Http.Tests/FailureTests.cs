using System.Net;
using System.Net.Http.Json;

namespace Sondor.Http.Tests;

/// <summary>Timeouts, cancellation, limits, an unreachable host and the exceptions that are values.</summary>
[TestFixture]
public sealed class FailureTests
{
    private static Task<HttpResponseMessage> NeverAnswers(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<HttpResponseMessage>(_ => throw new InvalidOperationException(), cancellationToken);

    [Test]
    public async Task GetAsync_Should_FailWithATimeout_When_TheCallTakesLongerThanAllowed()
    {
        var client = Clients.Create(new StubHandler(NeverAnswers), options => options.Timeout = TimeSpan.FromMilliseconds(50));

        var result = await client.GetAsync<Order>("orders/1");

        Assert.That(result.Error.Code, Is.EqualTo("Http.Timeout"));
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Failure));
        Assert.That(result.Error.GetHttpFailure()!.Status, Is.Null);
        Assert.That(result.Error.GetHttpFailure()!.Host, Is.EqualTo("orders.example.com"));
    }

    [Test]
    public async Task GetAsync_Should_UseTheTimeoutOfTheCall_InsteadOfTheClients()
    {
        var client = Clients.Create(new StubHandler(NeverAnswers), options => options.Timeout = TimeSpan.FromMinutes(5));

        var result = await client.GetAsync<Order>("orders/1", new HttpCallOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        Assert.That(result.Error.Code, Is.EqualTo("Http.Timeout"));
    }

    [Test]
    public async Task GetAsync_Should_ThrowWhatTheCallerCancelled_NotReturnIt()
    {
        var client = Clients.Create(new StubHandler(NeverAnswers));
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.CatchAsync<OperationCanceledException>(() => client.GetAsync<Order>("orders/1", cancellationToken: source.Token));
    }

    [Test]
    public async Task GetAsync_Should_ThrowWhatTheCallerCancelled_EvenWhenItWasCancelledBefore()
    {
        var client = Clients.Create(new StubHandler(NeverAnswers));
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.CatchAsync<OperationCanceledException>(() => client.GetAsync<Order>("orders/1", cancellationToken: source.Token));
    }

    [Test]
    public async Task GetAsync_Should_FailWithATimeout_When_TheHandlerThrowsATimeout()
    {
        var client = Clients.Create(new StubHandler((_, _) => throw new TimeoutException()));

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Http.Timeout"));
    }

    [Test]
    public async Task GetAsync_Should_FailWithATimeout_When_TheClientsOwnTimeoutPasses()
    {
        var http = new HttpClient(new StubHandler(NeverAnswers)) { BaseAddress = Clients.OrdersAddress, Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new HttpResultClient(http, new HttpResultOptions { Timeout = Timeout.InfiniteTimeSpan });

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Http.Timeout"));
    }

    [Test]
    public async Task GetAsync_Should_FailAsUnavailable_When_TheHostCannotBeReached()
    {
        var client = Clients.Create(new StubHandler((_, _) => throw new HttpRequestException("No such host is known: orders.internal.example:8443")));

        var error = (await client.GetAsync<Order>("a")).Error;

        Assert.That(error.Code, Is.EqualTo("Http.Unavailable"));
        Assert.That(error.Description, Does.Not.Contain("8443"));
    }

    [Test]
    public async Task GetAsync_Should_FailAsUnavailable_When_TheConnectionDropsWhileTheBodyIsRead()
    {
        var client = Clients.Create(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new BrokenContent { Headers = { ContentType = new("application/json") } }
        });

        var result = await client.GetAsync<Order>("a");

        Assert.That(result.Error.Code, Is.EqualTo("Http.Unavailable"));
        Assert.That(result.Error.GetHttpFailure()!.Status, Is.Null);
    }

    [Test]
    public async Task GetAsync_Should_FailAsUnavailable_When_TheConnectionDropsWhileAnErrorBodyIsRead()
    {
        var client = Clients.Create(() => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new BrokenContent { Headers = { ContentType = new("application/problem+json") } } });

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Http.Unavailable"));
    }

    [Test]
    public async Task GetAsync_Should_Throw_When_TheExceptionIsAMistakeAndNotAFailure()
    {
        var client = Clients.Create(new StubHandler((_, _) => throw new InvalidOperationException("a bug")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync<Order>("a"));
    }

    [Test]
    public async Task GetAsync_Should_ReportAnExceptionThatIsMapped_AndPreferTheMappingOfTheCaller()
    {
        var client = Clients.Create(
            new StubHandler((_, _) => throw new CircuitOpenException()),
            options => options.Exceptions
                .Map<CircuitOpenException>(_ => new Error("Orders.CircuitOpen", "The orders service is paused.", ErrorType.Failure))
                .Map<HttpRequestException>(_ => new Error("Orders.Down", "The orders service is down.", ErrorType.Failure)));
        var down = Clients.Create(
            new StubHandler((_, _) => throw new HttpRequestException()),
            options => options.Exceptions.Map<HttpRequestException>(_ => new Error("Orders.Down", "The orders service is down.", ErrorType.Failure)));

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Orders.CircuitOpen"));
        Assert.That((await down.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Orders.Down"));
    }

    [Test]
    public void Map_Should_Throw_When_TheMappingIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HttpResultOptions().Exceptions.Map<InvalidOperationException>(null!));
    }

    [Test]
    public async Task GetAsync_Should_FailAsTooLarge_When_TheLengthIsAboveTheLimit()
    {
        var client = Clients.Create(() => Responses.Json(HttpStatusCode.OK, $$"""{"id":"{{Guid.NewGuid()}}","name":"{{new string('a', 200)}}"}"""), options => options.MaxResponseBytes = 100);

        var error = (await client.GetAsync<Order>("a")).Error;

        Assert.That(error.Code, Is.EqualTo("Http.TooLarge"));
        Assert.That(error.GetHttpFailure()!.Status, Is.EqualTo(200));
    }

    [Test]
    public async Task GetAsync_Should_FailAsTooLarge_When_ABodyOfUnknownLengthPassesTheLimit()
    {
        var client = Clients.Create(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = EndlessContent.Of(50_000) }, options => options.MaxResponseBytes = 4096);

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Http.TooLarge"));
    }

    [Test]
    public async Task GetAsync_Should_ReadABodyThatIsAsLargeAsAllowed_OrLargerWhenTheCallAllowsIt()
    {
        var body = $$"""{"id":"{{Guid.NewGuid()}}","name":"widget"}""";
        var client = Clients.Create(() => Responses.Json(HttpStatusCode.OK, body), options => options.MaxResponseBytes = 10);

        Assert.That((await client.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Http.TooLarge"));
        Assert.That((await client.GetAsync<Order>("a", new HttpCallOptions { MaxResponseBytes = 1000 })).IsSuccess, Is.True);
    }

    [Test]
    public async Task GetAsync_Should_NotRejectAJsonBodyOfExactlyTheLimit()
    {
        var body = $$"""{"id":"{{Guid.NewGuid()}}","name":"widget"}""";
        var exact = System.Text.Encoding.UTF8.GetByteCount(body);
        var client = Clients.Create(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = EndlessJson(body) }, options => options.MaxResponseBytes = exact);

        Assert.That((await client.GetAsync<Order>("a")).IsSuccess, Is.True);

        static HttpContent EndlessJson(string text)
        {
            var content = new StreamContent(new NonSeekableStream(System.Text.Encoding.UTF8.GetBytes(text)));
            content.Headers.ContentType = new("application/json");

            return content;
        }
    }

    [Test]
    public async Task Client_Should_RejectACallTimeoutThatIsNotPositive()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.OK));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetAsync<Order>("a", new HttpCallOptions { Timeout = TimeSpan.Zero }));
    }

    [Test]
    public async Task Client_Should_AllowAnInfiniteTimeout()
    {
        var client = Clients.Create(() => Responses.Json(HttpStatusCode.OK, $$"""{"id":"{{Guid.NewGuid()}}","name":"w"}"""), options => options.Timeout = Timeout.InfiniteTimeSpan);

        Assert.That((await client.GetAsync<Order>("a")).IsSuccess, Is.True);
    }

    [Test]
    public async Task Client_Should_RejectBadArguments()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.OK));
        using var response = Responses.Empty(HttpStatusCode.OK);

        Assert.Throws<ArgumentNullException>(() => _ = new HttpResultClient(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SendAsync<Order>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ReadAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ReadAsync<Order>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.GetAsync<Order>(null!));
        Assert.Throws<ArgumentException>(() => _ = new HttpResultClient(new HttpClient(), new HttpResultOptions { MaxResponseBytes = 0 }));
    }

    [Test]
    public async Task SendAsync_Should_AcceptARequestThatIsAnAbsoluteUri_AndReportItsHost()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.NotFound));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://billing.example.com:8443/invoices?secret=1");

        var failure = (await client.SendAsync(request)).Error.GetHttpFailure()!;

        Assert.That(failure.Host, Is.EqualTo("billing.example.com:8443"));
    }

    [Test]
    public async Task ReadAsync_Should_ReportAnUnknownHost_When_NeitherTheResponseNorTheClientKnowsOne()
    {
        var client = new HttpResultClient(new HttpClient());
        using var response = Responses.Empty(HttpStatusCode.NotFound);

        Assert.That((await client.ReadAsync(response)).Error.GetHttpFailure()!.Host, Is.EqualTo("unknown"));
    }

    private sealed class CircuitOpenException : Exception;

    /// <summary>Streams its bytes without a length and without seeking, like a body on the wire.</summary>
    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, bytes.Length - _position);
            Array.Copy(bytes, _position, buffer, offset, read);
            _position += read;

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
