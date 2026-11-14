using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Sondor.Http.Tests;

[TestFixture]
public sealed class RegistrationTests
{
    private static readonly Uri Address = new("https://orders.example.com/v1/");

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection().AddLogging();

        register(services);

        return services.BuildServiceProvider();
    }

    [Test]
    public async Task AddSondorHttpClient_Should_BeValid_WithNoSettings()
    {
        await using var provider = Build(services => services
            .AddSondorHttpClient("orders")
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(() => Responses.Json(HttpStatusCode.OK, "[]"))));

        var client = provider.GetRequiredService<IHttpResultClientFactory>().CreateClient("orders");
        var result = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://orders.example.com/x"));

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void AddSondorHttpClient_Should_ApplyTheBaseAddressAndTheTimeout_ToTheHttpClient()
    {
        using var provider = Build(services => services.AddSondorHttpClient("orders", options =>
        {
            options.BaseAddress = Address;
            options.Timeout = TimeSpan.FromSeconds(7);
        }));

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("orders");

        Assert.That(http.BaseAddress, Is.EqualTo(Address));
        Assert.That(http.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
        Assert.That(http.DefaultRequestHeaders, Is.Empty);
    }

    [Test]
    public void AddSondorHttpClient_Should_KeepClientsApart_ByName()
    {
        using var provider = Build(services =>
        {
            services.AddSondorHttpClient("orders", options => options.BaseAddress = new Uri("https://orders.example.com/"));
            services.AddSondorHttpClient("invoices", options =>
            {
                options.BaseAddress = new Uri("https://invoices.example.com/");
                options.UseRemoteDetail = true;
            });
        });

        var monitor = provider.GetRequiredService<IOptionsMonitor<HttpResultOptions>>();

        Assert.That(monitor.Get("orders").BaseAddress!.Host, Is.EqualTo("orders.example.com"));
        Assert.That(monitor.Get("orders").UseRemoteDetail, Is.False);
        Assert.That(monitor.Get("invoices").UseRemoteDetail, Is.True);
    }

    [Test]
    public void AddSondorHttpClient_Should_AddToAClient_WhenCalledAgainWithTheSameName()
    {
        using var provider = Build(services =>
        {
            services.AddSondorHttpClient("orders", options => options.UseRemoteDetail = true);
            services.AddSondorHttpClient("orders", options => options.LogCalls = false);
        });

        var options = provider.GetRequiredService<IOptionsMonitor<HttpResultOptions>>().Get("orders");

        Assert.That(options.UseRemoteDetail, Is.True);
        Assert.That(options.LogCalls, Is.False);
    }

    [Test]
    public void AddSondorHttpClient_Should_BindTheOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Http:orders:BaseAddress"] = "https://orders.example.com/",
            ["Http:orders:Timeout"] = "00:00:05",
            ["Http:orders:MaxResponseBytes"] = "2048",
            ["Http:orders:UseRemoteDetail"] = "true",
            ["Http:orders:CodeByStatus:404"] = "Order.Missing",
            ["Http:orders:TypeByStatus:404"] = "Failure"
        }).Build();

        using var provider = Build(services =>
        {
            services.AddSondorHttpClient("orders");
            services.Configure<HttpResultOptions>("orders", configuration.GetSection("Http:orders"));
        });

        var options = provider.GetRequiredService<IOptionsMonitor<HttpResultOptions>>().Get("orders");

        Assert.That(options.BaseAddress, Is.EqualTo(new Uri("https://orders.example.com/")));
        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(options.MaxResponseBytes, Is.EqualTo(2048));
        Assert.That(options.UseRemoteDetail, Is.True);
        Assert.That(options.CodeByStatus[404], Is.EqualTo("Order.Missing"));
        Assert.That(options.CodeByStatus[400], Is.EqualTo("Request.Invalid"), "the defaults that were not changed stay");
        Assert.That(options.TypeByStatus[404], Is.EqualTo(ErrorType.Failure));
    }

    [Test]
    public async Task AddSondorHttpClient_Should_NotStartTheHost_When_TheOptionsAreNotValid()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSondorHttpClient("orders", options =>
        {
            options.Timeout = TimeSpan.FromSeconds(-5);
            options.MaxResponseBytes = 0;
            options.MaxErrorBodyBytes = -1;
            options.MaxRemoteDetailLength = -1;
            options.ErrorCodeMember = " ";
            options.BaseAddress = new Uri("/relative", UriKind.Relative);
            options.CodeByStatus[99] = "Bad.Status";
            options.CodeByStatus[400] = " ";
            options.TypeByStatus[700] = ErrorType.Failure;
            options.ErrorReaders.Add(null!);
            options.Json = null!;
        });

        using var host = builder.Build();
        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.That(failure!.Failures, Has.Some.Contains("Timeout"));
        Assert.That(failure.Failures, Has.Some.Contains("MaxResponseBytes"));
        Assert.That(failure.Failures, Has.Some.Contains("MaxErrorBodyBytes"));
        Assert.That(failure.Failures, Has.Some.Contains("MaxRemoteDetailLength"));
        Assert.That(failure.Failures, Has.Some.Contains("ErrorCodeMember"));
        Assert.That(failure.Failures, Has.Some.Contains("BaseAddress"));
        Assert.That(failure.Failures, Has.Some.Contains("CodeByStatus[99]"));
        Assert.That(failure.Failures, Has.Some.Contains("CodeByStatus[400]"));
        Assert.That(failure.Failures, Has.Some.Contains("TypeByStatus[700]"));
        Assert.That(failure.Failures, Has.Some.Contains("ErrorReaders"));
        Assert.That(failure.Failures, Has.Some.Contains("Json"));
    }

    [Test]
    public async Task AddSondorHttpClient_Should_StartTheHost_When_TheOptionsAreValid()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSondorHttpClient("orders", options => options.BaseAddress = Address);

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Test]
    public void CreateClient_Should_RejectABlankName()
    {
        using var provider = Build(services => services.AddSondorHttpClient("orders"));
        var factory = provider.GetRequiredService<IHttpResultClientFactory>();

        Assert.Throws<ArgumentException>(() => factory.CreateClient(" "));
        Assert.Throws<ArgumentNullException>(() => factory.CreateClient(null!));
    }

    [Test]
    public void AddSondorHttpClient_Should_RejectBadArguments()
    {
        Assert.Throws<ArgumentNullException>(() => HttpClientExtensions.AddSondorHttpClient(null!, "orders"));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddSondorHttpClient(" "));
    }

    [Test]
    public async Task AddSondorHttpClient_Should_GiveAClassItsClient_WhenItIsRegisteredByType()
    {
        await using var provider = Build(services =>
        {
            services.AddSingleton(new Greeting("hello"));
            services.AddSondorHttpClient<OrdersClient>("orders", options => options.BaseAddress = Address)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(() => Responses.Json(HttpStatusCode.OK, $$"""{"id":"{{Guid.Empty}}","name":"Widget"}""")));
        });

        var orders = provider.GetRequiredService<OrdersClient>();
        var result = await orders.Find();

        Assert.That(result.Value.Name, Is.EqualTo("Widget"));
        Assert.That(orders.Greeting, Is.EqualTo("hello"));
        Assert.That(provider.GetRequiredService<OrdersClient>(), Is.Not.SameAs(orders));
    }

    [Test]
    public async Task AddErrorReader_Should_AddAReaderThatIsBuiltWithItsDependencies_OrAnInstance()
    {
        await using var provider = Build(services =>
        {
            services.AddSingleton(new Greeting("from the container"));
            services.AddSondorHttpClient("orders", options => options.BaseAddress = Address)
                .AddErrorReader<GreetingReader>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(() => Responses.Empty(HttpStatusCode.Conflict)));
            services.AddSondorHttpClient("invoices", options => options.BaseAddress = Address)
                .AddErrorReader(new FixedReader("Invoice.Fixed"))
                .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(() => Responses.Empty(HttpStatusCode.Conflict)));
        });

        var factory = provider.GetRequiredService<IHttpResultClientFactory>();

        Assert.That((await factory.CreateClient("orders").GetAsync<Order>("a")).Error.Code, Is.EqualTo("from the container"));
        Assert.That((await factory.CreateClient("invoices").GetAsync<Order>("a")).Error.Code, Is.EqualTo("Invoice.Fixed"));
        Assert.Throws<ArgumentNullException>(() => HttpClientExtensions.AddErrorReader<GreetingReader>(null!));
        Assert.Throws<ArgumentNullException>(() => HttpClientExtensions.AddErrorReader(null!, new FixedReader("a")));
    }

    private sealed record Greeting(string Text);

    private sealed class OrdersClient(HttpResultClient http, Greeting greeting)
    {
        public string Greeting => greeting.Text;

        public Task<Result<Order>> Find() => http.GetAsync<Order>("orders/1");
    }

    private sealed class GreetingReader(Greeting greeting) : IHttpErrorReader
    {
        public ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Error?>(context.CreateError(greeting.Text, "A reader built by the container.", ErrorType.Conflict));
    }

    private sealed class FixedReader(string code) : IHttpErrorReader
    {
        public ValueTask<Error?> ReadAsync(HttpErrorContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Error?>(context.CreateError(code, "A fixed reader.", ErrorType.Conflict));
    }
}
