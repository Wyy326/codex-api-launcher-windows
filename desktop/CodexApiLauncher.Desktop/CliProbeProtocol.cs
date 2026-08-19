using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CodexApiLauncher.Desktop;

internal enum CliProbeMode
{
    StandardHttp,
    CliCompatible
}

internal sealed record CliProbeRequest(
    string BaseUrl,
    string Model,
    string ApiKey,
    CliProbeMode Mode = CliProbeMode.CliCompatible,
    TimeSpan? Timeout = null);

internal sealed record CliProbeProgress(string Phase, string Message, int ElapsedMs);

internal sealed class CliProbeResult
{
    public bool Ok { get; set; }
    public bool Cancelled { get; set; }
    public string Status { get; set; } = "";
    public int? HttpStatus { get; set; }
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public int LatencyMs { get; set; }
    public int? HeadersLatencyMs { get; set; }
    public int? FirstEventLatencyMs { get; set; }
    public string? FirstEventType { get; set; }
    public string? ResponseId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorType { get; set; }
    public string Details { get; set; } = "";
    public bool UsedCliIdentity { get; set; }
    public string ClientVersion { get; set; } = "";
}

internal static class CliProbeRequestBuilder
{
    public static HttpRequestMessage Build(
        CliProbeRequest probe,
        string version,
        string originator,
        string installationId,
        string windowId,
        string sessionId,
        string threadId,
        string requestId)
    {
        var endpoint = BuildEndpoint(probe.BaseUrl);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", probe.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var cliIdentity = probe.Mode == CliProbeMode.CliCompatible;
        var userAgent = cliIdentity
            ? $"{originator}/{version} (Windows; {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}) CodexApiLauncher"
            : "CodexApiLauncher/0.5";
        request.Headers.UserAgent.ParseAdd(userAgent);

        if (cliIdentity)
        {
            AddHeader(request, "originator", originator);
            AddHeader(request, "version", version);
            AddHeader(request, "OpenAI-Beta", "responses=experimental");
            AddHeader(request, "x-codex-window-id", windowId);
            AddHeader(request, "x-codex-installation-id", installationId);
            AddHeader(request, "session-id", sessionId);
            AddHeader(request, "thread-id", threadId);
            AddHeader(request, "x-client-request-id", requestId);
        }

        var metadata = cliIdentity
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["x-codex-installation-id"] = installationId,
                ["session-id"] = sessionId,
                ["thread-id"] = threadId,
                ["x-codex-window-id"] = windowId
            }
            : null;

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = probe.Model,
            ["instructions"] = "You are a connectivity probe. Reply exactly API_OK.",
            ["input"] = new[]
            {
                new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new { type = "input_text", text = "Reply exactly API_OK." }
                    }
                }
            },
            ["store"] = false,
            ["stream"] = true,
            ["tools"] = Array.Empty<object>(),
            ["tool_choice"] = "auto",
            ["parallel_tool_calls"] = false
        };
        if (metadata is not null)
        {
            payload["client_metadata"] = metadata;
        }

        var json = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    public static string BuildEndpoint(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("Base URL is required.", nameof(baseUrl));
        }

        var normalized = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Base URL must be an absolute HTTP or HTTPS URL.", nameof(baseUrl));
        }

        return normalized + "/responses";
    }

    private static void AddHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.TryAddWithoutValidation(name, value);
    }
}
