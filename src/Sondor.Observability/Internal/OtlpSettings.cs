namespace Sondor.Observability.Internal;

/// <summary>The OTLP settings once the section, the standard variables and the secret files have all had their say.</summary>
internal sealed record OtlpSettings(Uri Endpoint, OtlpProtocol Protocol, string? Headers, TimeSpan Timeout)
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The settings, or null when nothing is configured. Problems go to <paramref name="problems"/>, never with a header value.</summary>
    public static OtlpSettings? Resolve(OtlpOptions options, Ambient ambient, ICollection<string> problems)
    {
        var endpointText = Blank(options.Endpoint) ?? ambient.Variable("OTEL_EXPORTER_OTLP_ENDPOINT");

        Uri? endpoint = null;

        if (endpointText is not null &&
            (!Uri.TryCreate(endpointText, UriKind.Absolute, out endpoint) || endpoint.Scheme is not ("http" or "https")))
        {
            problems.Add("The OTLP endpoint must be an absolute http or https address.");
            endpoint = null;
        }

        var protocol = options.Protocol ?? ProtocolOf(ambient.Variable("OTEL_EXPORTER_OTLP_PROTOCOL"), problems);

        if (!Enum.IsDefined(protocol))
        {
            problems.Add("The OTLP protocol is not a known protocol.");
        }

        var headers = ResolveHeaders(options, ambient, problems);
        var timeout = options.Timeout ?? TimeoutOf(ambient.Variable("OTEL_EXPORTER_OTLP_TIMEOUT"), problems);

        if (timeout <= TimeSpan.Zero)
        {
            problems.Add("The OTLP timeout must be positive.");
        }

        return endpoint is null ? null : new OtlpSettings(endpoint, protocol, headers, timeout);
    }

    /// <summary>The address one signal is exported to. Over HTTP the signal path is part of it; over gRPC it is not.</summary>
    public Uri EndpointFor(string signal) =>
        Protocol == OtlpProtocol.HttpProtobuf
            ? new Uri($"{Endpoint.AbsoluteUri.TrimEnd('/')}/v1/{signal}")
            : Endpoint;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OtlpProtocol ProtocolOf(string? text, ICollection<string> problems)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "grpc":
                return OtlpProtocol.Grpc;
            case "http/protobuf":
                return OtlpProtocol.HttpProtobuf;
            default:
                problems.Add("OTEL_EXPORTER_OTLP_PROTOCOL must be 'grpc' or 'http/protobuf'.");
                return OtlpProtocol.Grpc;
        }
    }

    private static TimeSpan TimeoutOf(string? text, ICollection<string> problems)
    {
        if (text is null)
        {
            return DefaultTimeout;
        }

        if (int.TryParse(text, out var milliseconds) && milliseconds > 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        problems.Add("OTEL_EXPORTER_OTLP_TIMEOUT must be a number of milliseconds above zero.");
        return DefaultTimeout;
    }

    private static string? ResolveHeaders(OtlpOptions options, Ambient ambient, ICollection<string> problems)
    {
        string? headers;

        if (Blank(options.HeadersFile) is { } file)
        {
            if (Blank(options.Headers) is not null)
            {
                problems.Add("Set either the OTLP headers or the OTLP headers file, not both.");
            }

            headers = Secrets.ReadFile(file);

            if (headers is null)
            {
                problems.Add("The OTLP headers file does not exist or cannot be read.");
            }
        }
        else if (Blank(options.Headers) is { } value)
        {
            headers = Secrets.Resolve(value, ambient.SecretDirectories);
        }
        else
        {
            headers = ambient.Variable("OTEL_EXPORTER_OTLP_HEADERS");
        }

        if (headers is not null && !WellFormed(headers))
        {
            // Not echoed: a header is usually a key.
            problems.Add("The OTLP headers must be written as key=value, separated by commas.");
        }

        return headers;
    }

    private static bool WellFormed(string headers)
    {
        foreach (var pair in headers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (pair.IndexOf('=', StringComparison.Ordinal) < 1)
            {
                return false;
            }
        }

        return true;
    }
}
