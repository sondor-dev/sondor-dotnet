using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sondor.Http.Tests;

/// <summary>What a response becomes: a value, nothing, or an error with a stable code and a type.</summary>
[TestFixture]
public sealed class ResponseTests
{
    private static readonly Guid OrderId = Guid.Parse("6f0e3a5c-0b0a-4c4f-9f4e-3f6c1d2a7b11");

    [Test]
    public async Task GetAsync_Should_ReturnTheValue_When_TheAnswerIsJson()
    {
        var client = Clients.Create(() => Responses.Json(HttpStatusCode.OK, $$"""{"id":"{{OrderId}}","name":"Widget"}"""));

        var result = await client.GetAsync<Order>("orders/1");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(new Order(OrderId, "Widget")));
    }

    [Test]
    public async Task GetAsync_Should_ResolveThePathAgainstTheBaseAddress_AndSendNoDefaultHeadersOfItsOwn()
    {
        var handler = new StubHandler(() => Responses.Json(HttpStatusCode.OK, "[]"));
        var client = Clients.Create(handler);

        await client.GetAsync<List<Order>>("orders?page=2");

        Assert.That(handler.Requests[0].RequestUri, Is.EqualTo(new Uri("https://orders.example.com/orders?page=2")));
        Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(handler.Requests[0].Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task SendAsync_Should_NotReadTheBody_When_TheCallReturnsNoValue()
    {
        using var content = new ThrowingContent();
        var client = Clients.Create(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        var result = await client.DeleteAsync("orders/1");

        Assert.That(result.IsSuccess, Is.True);
    }

    [TestCase(HttpStatusCode.NoContent)]
    [TestCase(HttpStatusCode.OK)]
    public async Task GetAsync_Should_FailAsInvalidResponse_When_ThereIsNoBody(HttpStatusCode status)
    {
        var client = Clients.Create(() => Responses.Empty(status));

        var result = await client.GetAsync<Order>("orders/1");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Code, Is.EqualTo("Http.InvalidResponse"));
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Failure));
        Assert.That(result.Error.GetHttpFailure()?.Status, Is.EqualTo((int)status));
    }

    [TestCase("text/html", "<html>proxy</html>")]
    [TestCase("application/json", "not json")]
    [TestCase("application/json", "null")]
    [TestCase("application/json", "")]
    [TestCase("application/json", """{"id":"not a guid"}""")]
    public async Task GetAsync_Should_FailAsInvalidResponse_When_TheBodyIsNotWhatWasPromised(string mediaType, string body)
    {
        var client = Clients.Create(() => Responses.Json(HttpStatusCode.OK, body, mediaType));

        var result = await client.GetAsync<Order>("orders/1");

        Assert.That(result.Error.Code, Is.EqualTo("Http.InvalidResponse"));
    }

    [TestCase(400, "Request.Invalid", ErrorType.Validation)]
    [TestCase(401, "Request.Unauthorized", ErrorType.Unauthorized)]
    [TestCase(403, "Request.Forbidden", ErrorType.Forbidden)]
    [TestCase(404, "Request.NotFound", ErrorType.NotFound)]
    [TestCase(409, "Request.Conflict", ErrorType.Conflict)]
    [TestCase(415, "Request.UnsupportedMediaType", ErrorType.Failure)]
    [TestCase(422, "Request.Invalid", ErrorType.Validation)]
    [TestCase(429, "Request.RateLimited", ErrorType.RateLimited)]
    [TestCase(500, "Error.Internal", ErrorType.InternalServerError)]
    [TestCase(501, "Feature.Disabled", ErrorType.FeatureDisabled)]
    [TestCase(502, "Error.Internal", ErrorType.Failure)]
    [TestCase(503, "Request.Unavailable", ErrorType.Failure)]
    [TestCase(504, "Request.GatewayTimeout", ErrorType.Failure)]
    [TestCase(418, "Request.Failed", ErrorType.Failure)]
    [TestCase(508, "Error.Internal", ErrorType.InternalServerError)]
    [TestCase(304, "Request.Failed", ErrorType.Failure)]
    public async Task GetAsync_Should_UseTheStatus_When_TheResponseHasNoProblem(int status, string code, ErrorType type)
    {
        var client = Clients.Create(() => Responses.Empty((HttpStatusCode)status));

        var result = await client.GetAsync<Order>("orders/1");
        var error = result.Error;

        Assert.That(result.IsFailure, Is.True);
        Assert.That(error.Code, Is.EqualTo(code));
        Assert.That(error.Type, Is.EqualTo(type));
        Assert.That(error.Description, Is.Not.Empty);
        Assert.That(error.GetHttpFailure()?.Status, Is.EqualTo(status));
        Assert.That(error.GetHttpFailure()?.Problem, Is.Null);
    }

    [Test]
    public async Task SendAsync_Should_ReadTheProblemTheServerWrites()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.NotFound, """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
              "title": "Not Found",
              "status": 404,
              "detail": "No Order was found for the given Id.",
              "instance": "/orders/6f0e",
              "errorCode": "Order.NotFound",
              "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
            }
            """));

        var result = await client.GetAsync<Order>("orders/1");
        var failure = result.Error.GetHttpFailure();

        Assert.That(result.Error.Code, Is.EqualTo("Order.NotFound"));
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.NotFound));
        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Status, Is.EqualTo(404));
        Assert.That(failure.Method, Is.EqualTo("GET"));
        Assert.That(failure.Host, Is.EqualTo("orders.example.com"));
        Assert.That(failure.TraceId, Is.EqualTo("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"));
        Assert.That(failure.Problem!.Type, Is.EqualTo("https://tools.ietf.org/html/rfc9110#section-15.5.5"));
        Assert.That(failure.Problem.Title, Is.EqualTo("Not Found"));
        Assert.That(failure.Problem.Status, Is.EqualTo(404));
        Assert.That(failure.Problem.Detail, Is.EqualTo("No Order was found for the given Id."));
        Assert.That(failure.Problem.Instance, Is.EqualTo("/orders/6f0e"));
        Assert.That(failure.Problem.ErrorCode, Is.EqualTo("Order.NotFound"));
    }

    [Test]
    public async Task SendAsync_Should_NotShowTheOtherSidesDetail_ByDefault()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.Conflict,
            """{"errorCode":"Order.Late","detail":"Order 42 of jane@example.com is late."}"""));

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Description, Is.EqualTo("The request conflicts with the current state."));
        Assert.That(error.GetHttpFailure()!.Problem!.Detail, Is.EqualTo("Order 42 of jane@example.com is late."));
    }

    [Test]
    public async Task SendAsync_Should_UseTheOtherSidesDetail_When_ItIsSwitchedOn()
    {
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.Conflict, """{"errorCode":"Order.Late","detail":"  The order is late {0}\r\nand {braces}.  "}"""),
            options => options.UseRemoteDetail = true);

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Description, Is.EqualTo("The order is late {0}  and {braces}."));
    }

    [Test]
    public async Task SendAsync_Should_CutTheOtherSidesDetail_When_ItIsLongerThanAllowed()
    {
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.Conflict, $$"""{"detail":"{{new string('a', 500)}}"}"""),
            options =>
            {
                options.UseRemoteDetail = true;
                options.MaxRemoteDetailLength = 10;
            });

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Description, Is.EqualTo("aaaaaaaaaa…"));
    }

    [TestCase("""{"detail":"   "}""")]
    [TestCase("""{"detail":5}""")]
    [TestCase("""{"title":"Conflict"}""")]
    public async Task SendAsync_Should_FallBackToTheStatusText_When_TheDetailIsOfNoUse(string body)
    {
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.Conflict, body),
            options => options.UseRemoteDetail = true);

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Description, Is.EqualTo("The request conflicts with the current state."));
    }

    [TestCase("")]
    [TestCase("has space")]
    [TestCase("semi;colon")]
    [TestCase("new\nline")]
    [TestCase("éè")]
    public async Task SendAsync_Should_IgnoreACodeThatIsNotValid(string sent)
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.NotFound, JsonSerializer.Serialize(new { errorCode = sent })));

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Code, Is.EqualTo("Request.NotFound"));
    }

    [Test]
    public async Task SendAsync_Should_IgnoreACodeThatIsTooLong()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.NotFound, $$"""{"errorCode":"{{new string('a', 129)}}"}"""));

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Code, Is.EqualTo("Request.NotFound"));
        Assert.That(error.GetHttpFailure()!.Problem!.ErrorCode, Has.Length.EqualTo(129));
    }

    [Test]
    public async Task SendAsync_Should_AcceptACodeOfTheMaximumLength()
    {
        var code = new string('a', 128);
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.NotFound, $$"""{"errorCode":"{{code}}"}"""));

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Code, Is.EqualTo(code));
    }

    [Test]
    public async Task SendAsync_Should_ReadTheCodeFromTheMemberThatIsConfigured()
    {
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.Conflict, """{"code":"Order.Late","errorCode":"Other"}"""),
            options => options.ErrorCodeMember = "code");

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Code, Is.EqualTo("Order.Late"));
    }

    [Test]
    public async Task SendAsync_Should_KeepThePerPropertyErrors()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.BadRequest, """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": { "Quantity": ["Must be positive.", "Must be a whole number."], "Name": "Required.", "Odd": 7 }
            }
            """));

        var errors = (await client.GetAsync<Order>("orders/1")).Error.GetHttpFailure()!.Problem!.Errors;

        Assert.That(string.Join("|", errors["Quantity"]), Is.EqualTo("Must be positive.|Must be a whole number."));
        Assert.That(string.Join("|", errors["Name"]), Is.EqualTo("Required."));
        Assert.That(errors["Odd"], Is.Empty);
    }

    [Test]
    public async Task SendAsync_Should_BoundTheErrorsOfAProblem()
    {
        var properties = string.Join(',', Enumerable.Range(0, 150).Select(i => $"\"p{i}\":[{string.Join(',', Enumerable.Range(0, 25).Select(_ => $"\"{new string('m', 600)}\""))}]"));
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.BadRequest, "{\"errors\":{" + properties + "}}"),
            options => options.MaxErrorBodyBytes = 100_000_000);

        var errors = (await client.GetAsync<Order>("orders/1")).Error.GetHttpFailure()!.Problem!.Errors;

        Assert.That(errors, Has.Count.EqualTo(100));
        Assert.That(errors["p0"], Has.Count.EqualTo(20));
        Assert.That(errors["p0"][0], Has.Length.EqualTo(512));
    }

    [Test]
    public async Task SendAsync_Should_NotCountTheErrorsMemberAsAProblem_When_ItIsNotAnObject()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.BadRequest, """{"errors":["a"],"traceId":"abc"}"""));

        var failure = (await client.GetAsync<Order>("orders/1")).Error.GetHttpFailure()!;

        Assert.That(failure.Problem, Is.Null);
        Assert.That(failure.TraceId, Is.EqualTo("abc"));
    }

    [TestCase("""{"error":"x","message":"y"}""")]
    [TestCase("""["a","b"]""")]
    [TestCase("""{"status":"four hundred"}""")]
    [TestCase("""{"traceId":5,"title":""}""")]
    public async Task SendAsync_Should_UseTheStatus_When_TheJsonIsNotAProblem(string body)
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.BadRequest, body));

        var failure = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(failure.Code, Is.EqualTo("Request.Invalid"));
        Assert.That(failure.GetHttpFailure()!.Problem, Is.Null);
    }

    [Test]
    public async Task SendAsync_Should_UseTheStatus_When_TheErrorBodyIsHtmlOrBroken()
    {
        var html = Clients.Create(() => Responses.Json(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html"));
        var broken = Clients.Create(() => Responses.Problem(HttpStatusCode.BadRequest, "{\"errorCode\":"));
        var deep = Clients.Create(() => Responses.Problem(HttpStatusCode.BadRequest, new string('[', 100) + new string(']', 100)));

        Assert.That((await html.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Error.Internal"));
        Assert.That((await broken.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Request.Invalid"));
        Assert.That((await deep.GetAsync<Order>("a")).Error.Code, Is.EqualTo("Request.Invalid"));
    }

    [Test]
    public async Task SendAsync_Should_NotParseAnErrorBody_When_ItIsLargerThanAllowed()
    {
        var body = "{\"errorCode\":\"Order.Late\",\"detail\":\"" + new string('a', 200) + "\"}";
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.Conflict, body),
            options => options.MaxErrorBodyBytes = 64);

        var error = (await client.GetAsync<Order>("orders/1")).Error;

        Assert.That(error.Code, Is.EqualTo("Request.Conflict"));
        Assert.That(error.GetHttpFailure()!.Problem, Is.Null);
    }

    [Test]
    public async Task SendAsync_Should_NotReadAnErrorBody_When_TheLimitIsZero()
    {
        var client = Clients.Create(
            () => Responses.Problem(HttpStatusCode.Conflict, """{"errorCode":"Order.Late"}"""),
            options => options.MaxErrorBodyBytes = 0);

        Assert.That((await client.GetAsync<Order>("orders/1")).Error.Code, Is.EqualTo("Request.Conflict"));
    }

    [TestCase(HttpStatusCode.Conflict, 3)]
    [TestCase(HttpStatusCode.TooManyRequests, 120)]
    public async Task SendAsync_Should_ReportRetryAfterInSeconds(HttpStatusCode status, int seconds)
    {
        var client = Clients.Create(() =>
        {
            var response = Responses.Empty(status);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));

            return response;
        });

        var failure = (await client.GetAsync<Order>("a")).Error.GetHttpFailure()!;

        Assert.That(failure.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(seconds)));
    }

    [Test]
    public async Task SendAsync_Should_ReportRetryAfterAsADate_FromTheClockOfTheClient()
    {
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTime(now);
        var future = Clients.Create(
            new StubHandler(() => WithRetryAfter(now.AddSeconds(90))),
            time: clock);
        var past = Clients.Create(
            new StubHandler(() => WithRetryAfter(now.AddSeconds(-90))),
            time: clock);

        Assert.That((await future.GetAsync<Order>("a")).Error.GetHttpFailure()!.RetryAfter, Is.EqualTo(TimeSpan.FromSeconds(90)));
        Assert.That((await past.GetAsync<Order>("a")).Error.GetHttpFailure()!.RetryAfter, Is.EqualTo(TimeSpan.Zero));

        static HttpResponseMessage WithRetryAfter(DateTimeOffset date)
        {
            var response = Responses.Empty(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(date);

            return response;
        }
    }

    [Test]
    public async Task SendAsync_Should_ReportNoRetryAfter_When_TheHeaderIsMissing()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.ServiceUnavailable));

        Assert.That((await client.GetAsync<Order>("a")).Error.GetHttpFailure()!.RetryAfter, Is.Null);
    }

    [Test]
    public async Task Options_Should_ChangeTheCodeAndTheTypeOfAStatus()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.NotFound), options =>
        {
            options.CodeByStatus[404] = "Order.Missing";
            options.TypeByStatus[404] = ErrorType.Failure;
        });

        var error = (await client.GetAsync<Order>("a")).Error;

        Assert.That(error.Code, Is.EqualTo("Order.Missing"));
        Assert.That(error.Type, Is.EqualTo(ErrorType.Failure));
    }

    [Test]
    public async Task PostAsync_Should_SendTheBodyAsJson_AndReadTheAnswer()
    {
        string? sent = null;
        string? mediaType = null;
        var handler = new StubHandler(async (request, ct) =>
        {
            sent = await request.Content!.ReadAsStringAsync(ct);
            mediaType = request.Content.Headers.ContentType?.MediaType;

            return Responses.Json(HttpStatusCode.Created, $$"""{"id":"{{OrderId}}","name":"Widget"}""");
        });
        var client = Clients.Create(handler);

        var created = await client.PostAsync<Order, Order>("orders", new Order(OrderId, "Widget"));
        var put = await client.PutAsync<Order, Order>("orders/1", new Order(OrderId, "Widget"));
        var patched = await client.PatchAsync<Order, Order>("orders/1", new Order(OrderId, "Widget"));

        Assert.That(created.Value.Name, Is.EqualTo("Widget"));
        Assert.That(put.IsSuccess && patched.IsSuccess, Is.True);
        Assert.That(sent, Is.EqualTo($$"""{"id":"{{OrderId}}","name":"Widget"}"""));
        Assert.That(mediaType, Is.EqualTo("application/json"));
        Assert.That(handler.Requests.Select(r => r.Method), Is.EqualTo(new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch }));
    }

    [Test]
    public async Task PostAsync_Should_ReportOnlyWhetherItSucceeded_When_NoAnswerIsWanted()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.NoContent));

        Assert.That((await client.PostAsync("orders", new Order(OrderId, "Widget"))).IsSuccess, Is.True);
        Assert.That((await client.PutAsync("orders/1", new Order(OrderId, "Widget"))).IsSuccess, Is.True);
        Assert.That((await client.PatchAsync("orders/1", new Order(OrderId, "Widget"))).IsSuccess, Is.True);
    }

    [Test]
    public async Task PostAsync_Should_ReportAFailure_ForAStatusThatIsNotASuccess()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.Conflict, """{"errorCode":"Order.Exists"}"""));

        var result = await client.PostAsync("orders", new Order(OrderId, "Widget"));

        Assert.That(result.Error.Code, Is.EqualTo("Order.Exists"));
        Assert.That(result.Error.Type, Is.EqualTo(ErrorType.Conflict));
    }

    [Test]
    public async Task SendAsync_Should_UseTheJsonOptionsOfTheClient_IncludingASourceGeneratedContext()
    {
        var client = Clients.Create(
            () => Responses.Json(HttpStatusCode.OK, $$"""{"Id":"{{OrderId}}","Name":"Widget"}"""),
            options => options.Json = new JsonSerializerOptions { TypeInfoResolver = OrderContext.Default });

        var result = await client.GetAsync<Order>("orders/1");

        Assert.That(result.Value, Is.EqualTo(new Order(OrderId, "Widget")));
    }

    [Test]
    public async Task ReadAsync_Should_ApplyTheSameRules_ToAResponseFromAnotherClient()
    {
        var client = Clients.Create(() => Responses.Empty(HttpStatusCode.OK));
        using var success = Responses.Json(HttpStatusCode.OK, $$"""{"id":"{{OrderId}}","name":"Widget"}""");
        using var missing = Responses.Problem(HttpStatusCode.NotFound, """{"errorCode":"Order.NotFound"}""");
        using var again = Responses.Problem(HttpStatusCode.NotFound, """{"errorCode":"Order.NotFound"}""");
        using var bare = Responses.Empty(HttpStatusCode.OK);
        bare.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://orders.example.com/a?token=secret");

        var value = await client.ReadAsync<Order>(success);
        var failed = await client.ReadAsync<Order>(missing);
        var plain = await client.ReadAsync(bare);
        var plainFailure = await client.ReadAsync(again);

        Assert.That(value.Value.Name, Is.EqualTo("Widget"));
        Assert.That(failed.Error.Code, Is.EqualTo("Order.NotFound"));
        Assert.That(failed.Error.GetHttpFailure()!.Host, Is.EqualTo("orders.example.com"), "the host of the base address when the response knows no request");
        Assert.That(plain.IsSuccess, Is.True);
        Assert.That(plainFailure.Error.Code, Is.EqualTo("Order.NotFound"));
    }

    [Test]
    public void GetHttpFailure_Should_BeNull_ForAnErrorThatDidNotComeFromACall()
    {
        Assert.That(Error.NotFound("Order", "Id").GetHttpFailure(), Is.Null);
        Assert.That(Error.None.GetHttpFailure(), Is.Null);
    }

    [Test]
    public async Task Errors_Should_ShowNoValuesWhenPrinted()
    {
        var client = Clients.Create(() => Responses.Problem(HttpStatusCode.Conflict, """{"errorCode":"Order.Late","detail":"secret-text","traceId":"secret-trace"}"""));

        var error = (await client.GetAsync<Order>("a")).Error;
        var failure = error.GetHttpFailure()!;

        Assert.That(error.ToString(), Does.Not.Contain("secret"));
        Assert.That(failure.ToString(), Is.EqualTo("HttpFailure (status 409)"));
        Assert.That(failure.Problem!.ToString(), Is.EqualTo("RemoteProblem"));
    }

    [Test]
    public void RemoteProblem_Should_HoldWhatItIsGiven_ForAReaderOfAnotherFormat()
    {
        var problem = new RemoteProblem("t", "title", 400, "detail", "instance", "Code", new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["b"] });
        var empty = new RemoteProblem();

        Assert.That(string.Join("|", problem.Errors["a"]), Is.EqualTo("b"));
        Assert.That(empty.Errors, Is.Empty);
        Assert.That(empty.Type, Is.Null);
    }

    private sealed class ThrowingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("The body of a success without a value must not be read.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;

            return false;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

[JsonSerializable(typeof(Order))]
internal sealed partial class OrderContext : JsonSerializerContext;
