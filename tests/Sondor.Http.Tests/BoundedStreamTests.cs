using System.Net;
using Sondor.Http.Internal;

namespace Sondor.Http.Tests;

[TestFixture]
public sealed class BoundedStreamTests
{
    private static BoundedStream Of(int length, long limit) => new(new MemoryStream(new byte[length]), limit);

    [Test]
    public async Task Read_Should_PassBytesThrough_UpToTheLimit()
    {
        await using var stream = Of(10, 10);
        var buffer = new byte[10];

        Assert.That(stream.Read(buffer, 0, 4), Is.EqualTo(4));
        Assert.That(stream.Read(buffer.AsSpan(4)), Is.EqualTo(6));
        Assert.That(stream.Read(buffer, 0, 4), Is.Zero);
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.CanSeek, Is.False);
        Assert.That(stream.CanWrite, Is.False);
    }

    [Test]
    public async Task Read_Should_Throw_When_MoreThanTheLimitIsRead()
    {
        await using var sync = Of(10, 5);
        await using var async = Of(10, 5);
        await using var array = Of(10, 5);
        var buffer = new byte[10];

        Assert.Throws<ResponseTooLargeException>(() => Assert.That(sync.Read(buffer, 0, 10), Is.Zero));
        await Assert.ThrowsAsync<ResponseTooLargeException>(async () => Assert.That(await async.ReadAsync(buffer.AsMemory()), Is.Zero));
#pragma warning disable CA1835 // The array overload is the one under test: the stream overrides it.
        await Assert.ThrowsAsync<ResponseTooLargeException>(async () => Assert.That(await array.ReadAsync(buffer, 0, 10, CancellationToken.None), Is.Zero));
#pragma warning restore CA1835
    }

    [Test]
    public async Task Stream_Should_NotSupportWritingOrSeeking()
    {
        await using var stream = Of(1, 1);

        stream.Flush();

        Assert.Throws<NotSupportedException>(() => _ = stream.Length);
        Assert.Throws<NotSupportedException>(() => _ = stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
    }

    [Test]
    public async Task ReadAsync_Should_ReportAnInterruptedBody_ForACallThatReturnsNoValueToo()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.OK));
        using var broken = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new BrokenContent { Headers = { ContentType = new("application/problem+json") } } };
        using var brokenTyped = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new BrokenContent { Headers = { ContentType = new("application/problem+json") } } };

        var plain = await client.ReadAsync(broken);
        var typed = await client.ReadAsync<Order>(brokenTyped);

        Assert.That(plain.Error.Code, Is.EqualTo("Http.Unavailable"));
        Assert.That(typed.Error.Code, Is.EqualTo("Http.Unavailable"));
    }

    [Test]
    public async Task SendAsync_Should_ReportAFailureWithoutAValue_ForAnUnreachableHost()
    {
        var client = Clients.Create(new StubHandler((_, _) => throw new HttpRequestException()));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "orders/1");

        Assert.That((await client.SendAsync(request)).Error.Code, Is.EqualTo("Http.Unavailable"));
        Assert.That((await client.DeleteAsync("orders/1")).Error.Code, Is.EqualTo("Http.Unavailable"));
    }
}
