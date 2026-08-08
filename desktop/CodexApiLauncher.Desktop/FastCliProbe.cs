using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexApiLauncher.Desktop;

internal sealed class CodexCliProbe : IDisposable
{
    private const int MaxDetailLength = 1200;
    private static readonly Regex BearerTokenPattern = new(
        "\\bBearer\\s+[^\\s\\\"'\\\\,;}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ApiKeyPattern = new(
        @"\bsk-[A-Za-z0-9_-]{8,}",
        RegexOptions.CultureInvariant);
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly string version;
    private readonly string originator;
    private readonly string installationId;

    public CodexCliProbe(HttpClient? client = null, string? version = null, string? originator = null, string? installationId = null)
    {
        this.client = client ?? CreateClient();
        ownsClient = client is null;
        this.version = NormalizeVersion(version) ?? CodexCliIdentity.ResolveVersion();
        this.originator = NormalizeOriginator(originator);
        this.installationId = string.IsNullOrWhiteSpace(installationId)
            ? Guid.NewGuid().ToString()
            : installationId;
    }

    public string ClientVersion => version;

    public async Task<CliProbeResult> RunAsync(
        CliProbeRequest probe,
        IProgress<CliProbeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var endpoint = "";
        CliProbeResult result;
        try
        {
            if (string.IsNullOrWhiteSpace(probe.Model))
            {
                return NewResult(probe, endpoint, started, "model_missing", "当前 profile 没有配置模型。", false);
            }

            endpoint = CliProbeRequestBuilder.BuildEndpoint(probe.BaseUrl);
            var timeout = probe.Timeout ?? TimeSpan.FromSeconds(8);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var token = timeoutSource.Token;
            var request = CliProbeRequestBuilder.Build(
                probe,
                version,
                originator,
                installationId,
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString());

            using (request)
            {
                Report(progress, "connecting", "正在连接 Provider，等待响应头。", started);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    token).ConfigureAwait(false);

                var headersLatency = ElapsedMs(started);
                Report(progress, "headers", $"已收到 HTTP {((int)response.StatusCode)} 响应头。", started);
                result = NewResult(probe, endpoint, started, "", "", false);
                result.HttpStatus = (int)response.StatusCode;
                result.HeadersLatencyMs = headersLatency;
                result.UsedCliIdentity = probe.Mode == CliProbeMode.CliCompatible;
                result.ClientVersion = version;

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await ReadLimitedTextAsync(response.Content, token).ConfigureAwait(false);
                    return FinishHttpFailure(result, response.StatusCode, errorBody, probe.ApiKey, ElapsedMs(started));
                }

                Report(progress, "stream", "Provider 已接受请求，等待首个有效 SSE 事件。", started);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                if (contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    var sseResult = await ReadSseAsync(result, stream, progress, started, token).ConfigureAwait(false);
                    sseResult.Details = SanitizeDetail(sseResult.Details, probe.ApiKey);
                    return sseResult;
                }

                var body = await ReadLimitedTextAsync(stream, token).ConfigureAwait(false);
                return FinishJsonResponse(result, body, probe.ApiKey, ElapsedMs(started));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return NewResult(probe, endpoint, started, "cancelled", "检查已取消。", true);
        }
        catch (OperationCanceledException)
        {
            return NewResult(probe, endpoint, started, "timeout", "检查超时，Provider 没有在限定时间内返回。", false);
        }
        catch (HttpRequestException ex)
        {
            return NewResult(probe, endpoint, started, "unreachable", SanitizeDetail(ex.Message, probe.ApiKey), false);
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            return NewResult(probe, endpoint, started, "invalid_url", SanitizeDetail(ex.Message, probe.ApiKey), false);
        }
        catch (Exception ex)
        {
            return NewResult(probe, endpoint, started, "probe_failed", SanitizeDetail(ex.Message, probe.ApiKey), false);
        }
    }

    internal static HttpRequestMessage BuildRequestForTest(
        CliProbeRequest probe,
        string version,
        string originator,
        string installationId)
    {
        return CliProbeRequestBuilder.Build(
            probe,
            version,
            originator,
            installationId,
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString());
    }

    private async Task<CliProbeResult> ReadSseAsync(
        CliProbeResult result,
        Stream stream,
        IProgress<CliProbeProgress>? progress,
        Stopwatch started,
        CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var data = new StringBuilder();
        var eventName = "";

        while (true)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                if (data.Length > 0)
                {
                    var terminal = ProcessSseEvent(result, eventName, data.ToString(), progress, started);
                    if (terminal)
                    {
                        return result;
                    }
                }

                result.Status = "stream_incomplete";
                result.Details = "SSE 流已结束，但没有收到有效的输出或 response.completed。";
                result.LatencyMs = ElapsedMs(started);
                return result;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var terminal = ProcessSseEvent(result, eventName, data.ToString(), progress, started);
                    data.Clear();
                    eventName = "";
                    if (terminal)
                    {
                        return result;
                    }
                }

                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                eventName = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (data.Length < MaxDetailLength * 2)
                {
                    if (data.Length > 0)
                    {
                        data.Append('\n');
                    }
                    data.Append(line[5..].TrimStart());
                }
            }
        }
    }

    private bool ProcessSseEvent(
        CliProbeResult result,
        string eventName,
        string rawData,
        IProgress<CliProbeProgress>? progress,
        Stopwatch started)
    {
        result.FirstEventType ??= string.IsNullOrWhiteSpace(eventName) ? null : eventName;
        try
        {
            using var document = JsonDocument.Parse(rawData);
            var root = document.RootElement;
            var kind = GetString(root, "type") ?? eventName;
            if (result.FirstEventType is null && !string.IsNullOrWhiteSpace(kind))
            {
                result.FirstEventType = kind;
            }
            result.FirstEventLatencyMs ??= ElapsedMs(started);

            var response = GetObject(root, "response");
            result.ResponseId ??= GetString(response, "id");
            var error = GetObject(response, "error") ?? GetObject(root, "error");
            if (error is not null || ContainsOverloadMarker(root))
            {
                FinishStreamError(result, error, root, ElapsedMs(started));
                return true;
            }

            if (string.Equals(kind, "response.output_text.delta", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(GetString(root, "delta")))
            {
                result.Ok = true;
                result.Status = "passed";
                result.Details = "已收到当前模型的首个有效输出事件。";
                result.LatencyMs = ElapsedMs(started);
                return true;
            }

            if (string.Equals(kind, "response.completed", StringComparison.OrdinalIgnoreCase))
            {
                result.Ok = true;
                result.Status = "passed";
                result.Details = "已收到 response.completed。";
                result.LatencyMs = ElapsedMs(started);
                return true;
            }

            if (!string.IsNullOrWhiteSpace(kind))
            {
                Report(progress, "event", $"已收到 {kind}，Provider 正在处理。", started);
            }
        }
        catch (JsonException)
        {
            result.Status = "invalid_sse";
            result.Details = "收到无法解析的 SSE 数据。";
            result.LatencyMs = ElapsedMs(started);
            return true;
        }

        return false;
    }

    private CliProbeResult FinishStreamError(
        CliProbeResult result,
        JsonElement? error,
        JsonElement root,
        int elapsedMs)
    {
        var code = error.HasValue ? GetString(error.Value, "code") : null;
        var type = error.HasValue ? GetString(error.Value, "type") : null;
        var message = error.HasValue ? GetString(error.Value, "message") : null;
        var raw = root.ToString();
        result.ErrorCode = code;
        result.ErrorType = type;
        result.Status = ContainsOverloadMarker(root) || string.Equals(code, "server_is_overloaded", StringComparison.OrdinalIgnoreCase)
            ? "provider_overloaded"
            : "sse_failed";
        result.Details = SanitizeDetail(message ?? raw, "");
        result.LatencyMs = elapsedMs;
        return result;
    }

    private static CliProbeResult FinishHttpFailure(
        CliProbeResult result,
        HttpStatusCode code,
        string body,
        string apiKey,
        int elapsedMs)
    {
        result.Status = ClassifyHttpStatus(code, body);
        result.Details = string.IsNullOrWhiteSpace(body)
            ? $"Provider 返回 HTTP {(int)code}。"
            : SanitizeDetail(body, apiKey);
        result.LatencyMs = elapsedMs;
        return result;
    }

    private static CliProbeResult FinishJsonResponse(CliProbeResult result, string body, string apiKey, int elapsedMs)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            result.Status = "empty_response";
            result.Details = "Provider 返回了空响应体。";
            result.LatencyMs = elapsedMs;
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var error = GetObject(root, "error");
            if (error is not null || ContainsOverloadMarker(root))
            {
                result.Status = ContainsOverloadMarker(root) ? "provider_overloaded" : "provider_error";
                result.ErrorCode = error.HasValue ? GetString(error.Value, "code") : null;
                result.ErrorType = error.HasValue ? GetString(error.Value, "type") : null;
                result.Details = SanitizeDetail(error.HasValue ? GetString(error.Value, "message") ?? body : body, apiKey);
                result.LatencyMs = elapsedMs;
                return result;
            }

            if (root.ValueKind == JsonValueKind.Object && (root.TryGetProperty("id", out _)
                || root.TryGetProperty("output", out _)))
            {
                result.Ok = true;
                result.Status = "passed";
                result.Details = "Provider 返回了完整 Responses JSON。";
            }
            else
            {
                result.Status = "unexpected_response";
                result.Details = "Provider 返回了 JSON，但不是可识别的 Responses 响应。";
            }
        }
        catch (JsonException)
        {
            result.Status = "unexpected_response";
            result.Details = "Provider 返回了非 SSE、且无法解析的响应体。";
        }

        result.Details = SanitizeDetail(result.Details, apiKey);
        result.LatencyMs = elapsedMs;
        return result;
    }

    private static string ClassifyHttpStatus(HttpStatusCode code, string body)
    {
        var lower = body.ToLowerInvariant();
        return code switch
        {
            HttpStatusCode.BadRequest => "bad_request",
            HttpStatusCode.Unauthorized => "auth_failed",
            HttpStatusCode.Forbidden when lower.Contains("codex") || lower.Contains("official") || lower.Contains("cli") => "cli_only_rejected",
            HttpStatusCode.Forbidden => "forbidden",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed => "responses_unsupported",
            HttpStatusCode.RequestTimeout or (HttpStatusCode)429 => "rate_limited",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => "provider_unavailable",
            _ when (int)code >= 500 => "provider_unavailable",
            _ => "http_failed"
        };
    }

    private static async Task<string> ReadLimitedTextAsync(HttpContent content, CancellationToken token)
    {
        await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await ReadLimitedTextAsync(stream, token).ConfigureAwait(false);
    }

    private static async Task<string> ReadLimitedTextAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        using var output = new MemoryStream();
        while (output.Length < MaxDetailLength)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxDetailLength - (int)output.Length)), token).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            output.Write(buffer, 0, count);
        }
        return SanitizeDetail(Encoding.UTF8.GetString(output.ToArray()), "");
    }

    private static CliProbeResult NewResult(CliProbeRequest probe, string endpoint, Stopwatch started, string status, string details, bool cancelled)
    {
        return new CliProbeResult
        {
            Ok = false,
            Cancelled = cancelled,
            Status = status,
            Endpoint = endpoint,
            Model = probe.Model,
            LatencyMs = ElapsedMs(started),
            Details = SanitizeDetail(details, probe.ApiKey)
        };
    }

    private static void Report(IProgress<CliProbeProgress>? progress, string phase, string message, Stopwatch started)
    {
        progress?.Report(new CliProbeProgress(phase, message, ElapsedMs(started)));
    }

    private static int ElapsedMs(Stopwatch stopwatch)
    {
        return (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);
    }

    private static string? NormalizeVersion(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string NormalizeOriginator(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? "codex_cli_rs" : text;
    }

    private static string? GetString(JsonElement? element, string name)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object
            || !element.Value.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static JsonElement? GetObject(JsonElement? element, string name)
    {
        if (element.HasValue && element.Value.ValueKind == JsonValueKind.Object && element.Value.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }
        return null;
    }

    private static bool ContainsOverloadMarker(JsonElement element)
    {
        return element.ToString().Contains("server_is_overloaded", StringComparison.OrdinalIgnoreCase)
            || element.ToString().Contains("server overloaded", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeDetail(string? value, string apiKey)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            text = text.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
        }
        text = BearerTokenPattern.Replace(text, "Bearer [redacted]");
        text = ApiKeyPattern.Replace(text, "sk-[redacted]");
        return text.Length <= MaxDetailLength ? text : text[..MaxDetailLength] + "...";
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            client.Dispose();
        }
    }
}

internal static class CodexCliIdentity
{
    private const string FallbackVersion = "0.147.0";

    public static string ResolveVersion()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_CODEX_VERSION");
        if (IsVersion(configured))
        {
            return configured!.Trim();
        }

        foreach (var path in CandidatePackagePaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var value = document.RootElement.GetProperty("version").GetString();
                if (IsVersion(value))
                {
                    return value!.Trim();
                }
            }
            catch
            {
                // A missing or malformed package manifest should not block a probe.
            }
        }

        return FallbackVersion;
    }

    private static IEnumerable<string> CandidatePackagePaths()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_CODEX_PACKAGE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return configured;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            yield return Path.Combine(appData, "npm", "node_modules", "@openai", "codex", "package.json");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "nodejs", "node_modules", "@openai", "codex", "package.json");
        }
    }

    private static bool IsVersion(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value.Trim(), "^\\d+(\\.\\d+){1,3}(-[0-9A-Za-z.]+)?$", RegexOptions.CultureInvariant);
    }
}

internal static class ProtectedApiKeyReader
{
    public static string Read(string launcherHome, string profileId)
    {
        var secretsDir = Path.GetFullPath(Path.Combine(launcherHome, "secrets"));
        var path = Path.GetFullPath(Path.Combine(secretsDir, profileId + ".secret.json"));
        if (!path.StartsWith(secretsDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Profile ID 不是有效的安全文件名。");
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("没有找到该 profile 的受保护 API Key。", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var protectedText = document.RootElement.GetProperty("protectedApiKey").GetString();
        if (string.IsNullOrWhiteSpace(protectedText))
        {
            throw new InvalidOperationException("受保护 API Key 文件无效。");
        }

        byte[]? encrypted = null;
        byte[]? plain = null;
        try
        {
            encrypted = Convert.FromHexString(protectedText);
            plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.Unicode.GetString(plain);
        }
        finally
        {
            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
            if (plain is not null)
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
    }
}
