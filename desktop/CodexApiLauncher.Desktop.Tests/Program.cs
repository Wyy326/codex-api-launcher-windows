using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CodexApiLauncher.Desktop;

if (args.Length > 0 && string.Equals(args[0], "live", StringComparison.OrdinalIgnoreCase))
{
    return await ProbeTests.RunLiveAsync(args.Skip(1).ToArray());
}

return await ProbeTests.RunAsync(args.FirstOrDefault());

internal static class ProbeTests
{
    public static async Task<int> RunLiveAsync(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: live <profile-id> [cli|http]");
            return 64;
        }

        var profileId = args[0];
        var mode = args.Length > 1 && string.Equals(args[1], "http", StringComparison.OrdinalIgnoreCase)
            ? CliProbeMode.StandardHttp
            : CliProbeMode.CliCompatible;
        var home = Environment.GetEnvironmentVariable("CODEX_API_LAUNCHER_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexApiLauncher");
        }

        using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "profiles.json")));
        var profile = state.RootElement.GetProperty("profiles").EnumerateArray()
            .FirstOrDefault(value => string.Equals(value.GetProperty("id").GetString(), profileId, StringComparison.OrdinalIgnoreCase));
        if (profile.ValueKind == JsonValueKind.Undefined)
        {
            Console.Error.WriteLine($"Profile not found: {profileId}");
            return 66;
        }

        var baseUrl = profile.GetProperty("baseUrl").GetString() ?? "";
        var model = profile.GetProperty("model").GetString() ?? "";
        var apiKey = ProtectedApiKeyReader.Read(home, profileId);
        using var probe = new CodexCliProbe();
        var progress = new Progress<CliProbeProgress>(value =>
            Console.Error.WriteLine($"{value.ElapsedMs,5} ms  {value.Phase,-10} {value.Message}"));
        var result = await probe.RunAsync(
            new CliProbeRequest(baseUrl, model, apiKey, mode, TimeSpan.FromSeconds(12)),
            progress,
            CancellationToken.None);

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result.Ok ? 0 : 2;
    }

    public static async Task<int> RunAsync(string? selected)
    {
        var tests = new (string Name, Func<Task> Test)[]
        {
            ("builds CLI identity headers and a streamable Responses body", BuildsCliRequestAsync),
            ("returns on the first usable SSE output", ReturnsOnFirstOutputAsync),
            ("classifies an SSE response.failed overload", ClassifiesSseFailureAsync),
            ("measures SSE failures through the terminal event", MeasuresSseFailureLatencyAsync),
            ("classifies a CLI-only HTTP 403", ClassifiesCliOnly403Async),
            ("redacts credentials echoed by a provider", RedactsProviderDetailsAsync),
            ("honors cancellation while the stream is idle", HonorsCancellationAsync),
            ("migrates only known launcher provider IDs", MigratesOnlyKnownProviderIdsAsync)
        };

        if (!string.IsNullOrWhiteSpace(selected))
        {
            tests = tests.Where((_, index) => index.ToString() == selected).ToArray();
        }

        var failed = 0;
        foreach (var (name, test) in tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
            }
        }

        Console.WriteLine($"{tests.Length - failed}/{tests.Length} probe tests passed");
        return failed == 0 ? 0 : 1;
    }

    private static Task BuildsCliRequestAsync()
    {
        var request = CodexCliProbe.BuildRequestForTest(
            new CliProbeRequest("https://example.test/v1", "gpt-test", "secret-value", CliProbeMode.CliCompatible),
            version: "0.147.0",
            originator: "codex_cli_rs",
            installationId: "installation-test");

        Assert(request.RequestUri?.AbsoluteUri == "https://example.test/v1/responses", "endpoint mismatch");
        Assert(request.Headers.Authorization?.Scheme == "Bearer", "authorization scheme missing");
        Assert(request.Headers.Authorization?.Parameter == "secret-value", "authorization value missing");
        Assert(request.Headers.TryGetValues("originator", out var originators)
            && originators.Single() == "codex_cli_rs", "originator header mismatch");
        Assert(request.Headers.UserAgent.Any(value => value.Product?.Name == "codex_cli_rs"
            && value.Product.Version?.ToString() == "0.147.0"), "Codex user agent mismatch");
        Assert(request.Headers.Contains("x-codex-window-id"), "window fingerprint missing");
        Assert(request.Headers.Contains("x-codex-installation-id"), "installation fingerprint missing");
        Assert(request.Headers.Contains("OpenAI-Beta"), "Responses beta header missing");

        var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var root = json.RootElement;
        Assert(root.GetProperty("model").GetString() == "gpt-test", "model mismatch");
        Assert(root.GetProperty("stream").GetBoolean(), "stream must be enabled");
        Assert(!string.IsNullOrWhiteSpace(root.GetProperty("instructions").GetString()), "instructions missing");
        Assert(root.GetProperty("input").ValueKind == JsonValueKind.Array, "CLI input must be an array");
        Assert(root.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString() == "input_text", "input text shape mismatch");
        Assert(!root.ToString().Contains("secret-value", StringComparison.Ordinal), "API key leaked into body");
        request.Dispose();
        return Task.CompletedTask;
    }

    private static async Task ReturnsOnFirstOutputAsync()
    {
        var sse = "data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_1\"}}\n\n"
            + "data: {\"type\":\"response.output_text.delta\",\"delta\":\"API_OK\"}\n\n";
        using var client = CreateClient(new StaticHandler(HttpStatusCode.OK, sse, "text/event-stream"));
        var probe = new CodexCliProbe(client, "0.147.0");

        var result = await probe.RunAsync(
            new CliProbeRequest("https://example.test/v1", "gpt-test", "key", CliProbeMode.CliCompatible),
            progress: null,
            CancellationToken.None);

        Assert(result.Ok, "first output should pass");
        Assert(result.Status == "passed", "unexpected success status");
        Assert(result.FirstEventType == "response.created", "first event was not recorded");
        Assert(result.ResponseId == "resp_1", "response id was not captured");
    }

    private static async Task ClassifiesSseFailureAsync()
    {
        var sse = "data: {\"type\":\"response.failed\",\"response\":{\"id\":\"resp_2\",\"error\":{\"code\":\"server_is_overloaded\",\"message\":\"busy\"}}}\n\n";
        using var client = CreateClient(new StaticHandler(HttpStatusCode.OK, sse, "text/event-stream"));
        var probe = new CodexCliProbe(client, "0.147.0");

        var result = await probe.RunAsync(
            new CliProbeRequest("https://example.test/v1", "gpt-test", "key", CliProbeMode.CliCompatible),
            progress: null,
            CancellationToken.None);

        Assert(!result.Ok, "failed SSE must not pass");
        Assert(result.Status == "provider_overloaded", "overload event was not classified");
        Assert(result.ErrorCode == "server_is_overloaded", "SSE error code missing");
        Assert(result.Details.Contains("busy", StringComparison.Ordinal), "SSE error message missing");
    }

    private static async Task ClassifiesCliOnly403Async()
    {
        using var client = CreateClient(new StaticHandler(
            HttpStatusCode.Forbidden,
            "This account only allows Codex official clients",
            "application/json"));
        var probe = new CodexCliProbe(client, "0.147.0");

        var result = await probe.RunAsync(
            new CliProbeRequest("https://example.test/v1", "gpt-test", "key", CliProbeMode.CliCompatible),
            progress: null,
            CancellationToken.None);

        Assert(!result.Ok, "403 must not pass");
        Assert(result.Status == "cli_only_rejected", "CLI-only 403 was not classified");
        Assert(result.HttpStatus == 403, "HTTP status missing");
    }

    private static async Task MeasuresSseFailureLatencyAsync()
    {
        var chunks = new[]
        {
            "data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_delayed\"}}\n\n",
            "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"server_is_overloaded\",\"message\":\"busy\"}}}\n\n"
        };
        using var client = CreateClient(new DelayedSseHandler(chunks, TimeSpan.FromMilliseconds(120)));
        var probe = new CodexCliProbe(client, "0.147.0");

        var result = await probe.RunAsync(
            new CliProbeRequest("https://example.test/v1", "gpt-test", "key", CliProbeMode.CliCompatible),
            progress: null,
            CancellationToken.None);

        var firstEventLatency = result.FirstEventLatencyMs
            ?? throw new InvalidOperationException("first event latency missing");
        Assert(result.LatencyMs >= firstEventLatency + 80,
            $"total latency {result.LatencyMs} ms did not include the delayed terminal event");
    }

    private static async Task RedactsProviderDetailsAsync()
    {
        const string exactKey = "exact-provider-secret";
        const string bearerToken = "header-token-value";
        var skToken = string.Concat("s", "k-", "ThisMustNeverReachTheUi123456");
        var body = $"Provider echoed Authorization: Bearer {bearerToken}; key={skToken}; exact={exactKey}";
        using var client = CreateClient(new StaticHandler(HttpStatusCode.BadRequest, body, "text/plain"));
        var probe = new CodexCliProbe(client, "0.147.0");

        var result = await probe.RunAsync(
            new CliProbeRequest("https://example.test/v1", "gpt-test", exactKey, CliProbeMode.CliCompatible),
            progress: null,
            CancellationToken.None);

        Assert(!result.Details.Contains(exactKey, StringComparison.Ordinal), "exact API key leaked");
        Assert(!result.Details.Contains(bearerToken, StringComparison.Ordinal), "Bearer token leaked");
        Assert(!result.Details.Contains(skToken, StringComparison.Ordinal), "sk token leaked");
    }

    private static async Task HonorsCancellationAsync()
    {
        using var client = CreateClient(new StaticHandler(
            HttpStatusCode.OK,
            "data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_3\"}}\n\n",
            "text/event-stream",
            blockAfterBody: true));
        var probe = new CodexCliProbe(client, "0.147.0");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        var result = await probe.RunAsync(
            new CliProbeRequest("https://example.test/v1", "gpt-test", "key", CliProbeMode.CliCompatible),
            progress: null,
            cts.Token);

        Assert(result.Cancelled, "cancellation flag missing");
        Assert(result.Status == "cancelled", "cancellation status mismatch");
    }

    private static Task MigratesOnlyKnownProviderIdsAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-provider-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "state_1.sqlite");
            using (var database = CodexConversationProviderMigration.NativeSqliteDatabase.Open(databasePath, create: true))
            {
                database.Execute("CREATE TABLE threads (id TEXT PRIMARY KEY, model_provider TEXT);");
                database.Execute("CREATE TABLE external_agent_config_imports (provider_id TEXT);");
                database.Execute("INSERT INTO threads VALUES ('old-1', 'api_old'), ('old-2', 'api_old'), ('other', 'openai'), ('current', 'api_codex_launcher');");
                database.Execute("INSERT INTO external_agent_config_imports VALUES ('api_old'), ('openai');");
            }

            var result = CodexConversationProviderMigration.Run(
                root,
                "api_codex_launcher",
                new[] { "api_old" });

            Assert(result.Status == "migrated", "migration did not run");
            Assert(result.ThreadRowsChanged == 2, "unexpected migrated thread count");
            Assert(result.ExternalRowsChanged == 1, "unexpected external import count");
            Assert(File.Exists(Path.Combine(result.BackupDirectory, "state_1.sqlite")), "database backup missing");

            using var migrated = CodexConversationProviderMigration.NativeSqliteDatabase.Open(databasePath);
            Assert(migrated.QueryInt("SELECT COUNT(*) FROM threads WHERE model_provider = 'api_codex_launcher';") == 3,
                "known legacy threads were not unified");
            Assert(migrated.QueryInt("SELECT COUNT(*) FROM threads WHERE model_provider = 'openai';") == 1,
                "unrelated provider thread was modified");
            Assert(migrated.QueryInt("SELECT COUNT(*) FROM external_agent_config_imports WHERE provider_id = 'openai';") == 1,
                "unrelated external provider was modified");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed class StaticHandler : HttpMessageHandler
{
    private readonly HttpStatusCode statusCode;
    private readonly string body;
    private readonly string contentType;
    private readonly bool blockAfterBody;

    public StaticHandler(HttpStatusCode statusCode, string body, string contentType, bool blockAfterBody = false)
    {
        this.statusCode = statusCode;
        this.body = body;
        this.contentType = contentType;
        this.blockAfterBody = blockAfterBody;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("The probe must use the async HTTP path.");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StreamContent(new BlockingStream(Encoding.UTF8.GetBytes(body), blockAfterBody, cancellationToken))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return Task.FromResult(response);
    }
}

internal sealed class BlockingStream : Stream
{
    private readonly byte[] data;
    private readonly bool blockAfterBody;
    private readonly CancellationToken cancellationToken;
    private int position;

    public BlockingStream(byte[] data, bool blockAfterBody, CancellationToken cancellationToken)
    {
        this.data = data;
        this.blockAfterBody = blockAfterBody;
        this.cancellationToken = cancellationToken;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        => await ReadCoreAsync(buffer, cancellationToken);

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken readToken)
    {
        if (position >= data.Length)
        {
            if (!blockAfterBody)
            {
                return 0;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(this.cancellationToken, readToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
            return 0;
        }

        var count = Math.Min(buffer.Length, data.Length - position);
        data.AsMemory(position, count).CopyTo(buffer);
        position += count;
        await Task.Yield();
        return count;
    }
}

internal sealed class DelayedSseHandler : HttpMessageHandler
{
    private readonly string[] chunks;
    private readonly TimeSpan delay;

    public DelayedSseHandler(string[] chunks, TimeSpan delay)
    {
        this.chunks = chunks;
        this.delay = delay;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DelayedChunksStream(chunks, delay))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return Task.FromResult(response);
    }
}

internal sealed class DelayedChunksStream : Stream
{
    private readonly byte[][] chunks;
    private readonly TimeSpan delay;
    private int index;

    public DelayedChunksStream(IEnumerable<string> chunks, TimeSpan delay)
    {
        this.chunks = chunks.Select(Encoding.UTF8.GetBytes).ToArray();
        this.delay = delay;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => ReadCoreAsync(buffer, cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (index >= chunks.Length)
        {
            return 0;
        }

        if (index > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        var chunk = chunks[index++];
        if (chunk.Length > buffer.Length)
        {
            throw new InvalidOperationException("Test chunk exceeds the requested read buffer.");
        }
        chunk.CopyTo(buffer);
        return chunk.Length;
    }
}
