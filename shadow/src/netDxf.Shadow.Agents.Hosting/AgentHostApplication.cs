using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Features;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Agents.Auth;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.AgentHost;

public static class AgentHostApplication
{
    public static WebApplication Build(AgentHostOptions options, Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        // Request logging can include the OAuth callback query. The companion intentionally logs only explicit startup text.
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, options.Port);
            server.Limits.MaxRequestBodySize = 8 * 1024 * 1024;
            server.Limits.MaxConcurrentConnections = 64;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<BrowserStudioRelay>();
        builder.Services.AddSingleton<ExternalAgentHost>();
        // Sign-in has its own finite timeout; streaming inference uses the separate long-lived HTTP transport below.
        builder.Services.AddSingleton(_ => new ChatGptSignInClient());
        builder.Services.AddSingleton<ChatGptHostSession>();
        if (options.Workspace is not null) builder.Services.AddSingleton<CompanionWorkspace>();
        builder.Services.AddSingleton(new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(30),
        }) { Timeout = Timeout.InfiniteTimeSpan });
        builder.Services.AddMcpServer(serverOptions => { })
            .WithHttpTransport();
        // The configured handlers resolve the same dynamic executor that owns the registered browser connection.
        builder.Services.AddOptions<ModelContextProtocol.Server.McpServerOptions>()
            .Configure<BrowserStudioRelay>((serverOptions, relay) => ShadowMcpServer.Configure(serverOptions, relay));
        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        _ = app.Services.GetRequiredService<ChatGptHostSession>(); // Validate and persist the stable nonsecret host identity before listening.
        if (options.Workspace is not null) _ = app.Services.GetRequiredService<CompanionWorkspace>();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
                || context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != options.Port
                || context.Request.Headers.Origin.Count > 1
                || (origin.Length != 0 && !options.AllowedOrigins.Contains(origin)))
            {
                await ErrorAsync(context, 403, "origin_rejected", "This host accepts only its loopback address and explicitly allowed studio origins.");
                return;
            }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (origin.Length != 0)
            {
                context.Response.Headers.AccessControlAllowOrigin = origin;
                context.Response.Headers.Vary = "Origin";
                context.Response.Headers.AccessControlExposeHeaders = "Content-Type, Retry-After, Mcp-Session-Id";
            }
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                var requestedHeaders = context.Request.Headers.AccessControlRequestHeaders.ToString();
                if (origin.Length == 0 || requestedHeaders.Length > 4096 || requestedHeaders.Any(c => c is '\r' or '\n'))
                {
                    await ErrorAsync(context, 403, "preflight_rejected", "A preflight requires an allowed origin and bounded headers."); return;
                }
                context.Response.Headers.AccessControlAllowMethods = "GET, POST, DELETE, OPTIONS";
                if (requestedHeaders.Length != 0) context.Response.Headers.AccessControlAllowHeaders = requestedHeaders;
                context.Response.Headers.AccessControlMaxAge = "600";
                if (context.Request.Headers["Access-Control-Request-Private-Network"] == "true")
                    context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
                context.Response.StatusCode = 204; return;
            }
            var callback = context.Request.Path.Equals("/auth/callback", StringComparison.Ordinal);
            var studioSocket = context.Request.Path.Equals("/studio", StringComparison.Ordinal) && context.WebSockets.IsWebSocketRequest;
            if (!callback && !studioSocket)
            {
                var header = context.Request.Headers.Authorization.ToString();
                if (context.Request.Headers.Authorization.Count != 1 || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    || !options.ValidateToken(header[7..]))
                {
                    context.Response.Headers.WWWAuthenticate = "Bearer realm=\"netDxf Shadow Agent Host\"";
                    await ErrorAsync(context, 401, "bridge_auth_required", "Supply the companion's bridge token."); return;
                }
            }
            await next(context);
        });

        app.MapGet("/health", (BrowserStudioRelay relay) => new
        {
            status = "ok", studioConnected = relay.IsConnected, toolCount = relay.Tools.Count, pendingCalls = relay.PendingCount,
            workspaceEnabled = options.Workspace is not null, processesEnabled = options.AllowProcesses,
        });
        if (options.Workspace is not null)
        {
            app.MapGet("/workspace/tools", (CompanionWorkspace workspace) => workspace.Tools);
            app.MapPost("/workspace/call", async (HttpContext context, CompanionWorkspace workspace) =>
            {
                try
                {
                    using var request = await JsonDocument.ParseAsync(context.Request.Body,
                        new JsonDocumentOptions { MaxDepth = 64 }, context.RequestAborted);
                    var root = request.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out var name)
                        || name.ValueKind != JsonValueKind.String || name.GetString() is not { Length: > 0 and <= 128 } toolName)
                    { await ErrorAsync(context, 400, "invalid_request", "Supply a registered companion tool name and object arguments."); return; }
                    var arguments = root.TryGetProperty("arguments", out var value) ? value : McpJson.EmptyObject;
                    var result = await workspace.ExecuteAsync(toolName, arguments, context.RequestAborted);
                    await context.Response.WriteAsJsonAsync(result, McpJson.Options, context.RequestAborted);
                }
                catch (JsonException) { await ErrorAsync(context, 400, "invalid_request", "The workspace request must contain valid bounded JSON."); }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
            });
        }
        else app.MapGet("/workspace/tools", () => Array.Empty<McpToolDefinition>());
        app.MapGet("/auth/status", (ChatGptHostSession session) => session.Status);
        app.MapPost("/auth/login", async (HttpContext context, ChatGptHostSession session) =>
        {
            try
            {
                var authorization = await session.BeginAsync(context.RequestAborted);
                await context.Response.WriteAsJsonAsync(new { authorizationUrl = authorization.AbsoluteUri }, context.RequestAborted);
            }
            catch (Exception error) when (error is HttpRequestException or ChatGptAuthenticationException or JsonException)
            { await ErrorAsync(context, 502, "login_unavailable", session.Status.Error ?? "ChatGPT sign-in is temporarily unavailable."); }
        });
        app.MapPost("/auth/logout", async (HttpContext context, ChatGptHostSession session) =>
        {
            var revoked = await session.LogoutAsync(context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { authenticated = false, revoked }, context.RequestAborted);
        });
        app.MapGet("/auth/callback", async (HttpContext context, ChatGptHostSession session) =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            try
            {
                await session.CompleteAsync(new Uri(options.CallbackUri + context.Request.QueryString.Value), context.RequestAborted);
                await context.Response.WriteAsync("ChatGPT sign-in is complete. Return to netDxf Shadow Studio; this window can be closed.", context.RequestAborted);
            }
            catch (Exception error) when (error is ChatGptAuthenticationException or HttpRequestException or JsonException or CryptographicException)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(session.Status.Error ?? "ChatGPT sign-in failed. Return to the studio and start again.", context.RequestAborted);
            }
        });
        app.MapPost("/v1/responses", (HttpContext context, ChatGptHostSession session, HttpClient http) => ProxyAsync(context, session, http, models: false));
        app.MapGet("/v1/models", (HttpContext context, ChatGptHostSession session, HttpClient http) => ProxyAsync(context, session, http, models: true));
        app.MapPost("/providers/stream", ProviderInferenceAsync);
        app.Map("/studio", async (HttpContext context, BrowserStudioRelay relay) =>
        {
            if (!context.WebSockets.IsWebSocketRequest || context.Request.Headers.Origin.Count != 1)
            { await ErrorAsync(context, 400, "websocket_required", "The studio connection requires a WebSocket from an allowed origin."); return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await relay.ConnectAsync(socket, options, context.RequestAborted);
        });
        app.MapExternalAgents();
        app.MapMcp("/mcp");
        return app;
    }

    private static async Task ProviderInferenceAsync(HttpContext context, HttpClient http)
    {
        ShadowAgentBridgeRequest? request = null;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ShadowAgentBridgeRequest>(context.Request.Body,
                ShadowAgentBridgeRequest.JsonOptions, context.RequestAborted).ConfigureAwait(false);
            if (request is null) throw new ArgumentException("Supply a typed provider inference request.");
            request.Validate();
            using var client = ShadowAgentProviders.Create(new()
            {
                Provider = request.Provider, ApiKey = request.ApiKey, Model = request.Model, Endpoint = request.Endpoint,
                OptimizePromptCaching = request.OptimizePromptCaching, PromptCacheKey = request.PromptCacheKey,
            }, http);
            context.Response.ContentType = "application/x-ndjson; charset=utf-8";
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await foreach (var update in client.GetStreamingResponseAsync(request.Messages, request.CreateOptions(), context.RequestAborted).ConfigureAwait(false))
                await WriteProviderFrameAsync(context, new("update", update)).ConfigureAwait(false);
            await WriteProviderFrameAsync(context, new("completed")).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Abort(); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            var transient = ShadowAgentProviders.TryGetTransientFailure(error, out var retryAfter);
            var message = request?.ApiKey is { Length: > 0 } key ? error.Message.Replace(key, "[redacted]", StringComparison.Ordinal) : error.Message;
            if (message.Length > 4096) message = message[..4096];
            if (!context.Response.HasStarted) context.Response.ContentType = "application/x-ndjson; charset=utf-8";
            await WriteProviderFrameAsync(context, new("error", Code: error is ShadowAgentProviderException provider ? provider.Code : "provider_request_failed",
                Message: message, IsTransient: transient, RetryAfterSeconds: retryAfter?.TotalSeconds)).ConfigureAwait(false);
        }
    }

    private static async Task WriteProviderFrameAsync(HttpContext context, ShadowAgentBridgeFrame frame)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(frame, ShadowAgentBridgeRequest.JsonOptions);
        if (payload.Length > 4 * 1024 * 1024) throw new InvalidOperationException("The provider update exceeded the companion's frame budget.");
        await context.Response.Body.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.WriteAsync("\n"u8.ToArray(), context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task ProxyAsync(HttpContext context, ChatGptHostSession session, HttpClient http, bool models)
    {
        try
        {
            var lease = await session.GetLeaseAsync(context.RequestAborted).ConfigureAwait(false);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lease.SessionCancellation);
            using var request = new HttpRequestMessage(models ? HttpMethod.Get : HttpMethod.Post,
                models ? "https://api.openai.com/v1/models" : "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", lease.Credential.AccessToken);
            if (!models)
            {
                var root = await JsonNode.ParseAsync(context.Request.Body, documentOptions: new JsonDocumentOptions { MaxDepth = 64 }, cancellationToken: cancellation.Token).ConfigureAwait(false) as JsonObject;
                if (root is null || root["model"]?.GetValue<string>() is not { Length: > 0 and <= 256 })
                { await ErrorAsync(context, 400, "invalid_request", "A Responses request must contain a model and input context."); return; }
                ChatGptResponsesContract.Normalize(root);
                request.Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json");
                request.Headers.Accept.Add(new("text/event-stream"));
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            if (response.Headers.TryGetValues("Retry-After", out var retry)) context.Response.Headers.RetryAfter = string.Join(",", retry);
            context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token).ConfigureAwait(false);
            var buffer = new byte[16_384];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false)) != 0)
            {
                await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellation.Token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (ChatGptAuthenticationException error) { await ErrorAsync(context, 401, error.Code, error.Message); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { await ErrorAsync(context, 400, "invalid_request", "The request does not match the supported ChatGPT Responses contract."); }
        catch (HttpRequestException)
        {
            if (context.Response.HasStarted) context.Abort();
            else await ErrorAsync(context, 502, "upstream_unavailable", "OpenAI could not be reached. This inference request was not replayed.");
        }
        catch (OperationCanceledException)
        {
            if (context.Response.HasStarted || context.RequestAborted.IsCancellationRequested) context.Abort();
            else await ErrorAsync(context, 401, "session_ended", "The ChatGPT session ended while processing this request.");
        }
    }

    private static async Task ErrorAsync(HttpContext context, int status, string code, string message)
    {
        if (context.Response.HasStarted) { context.Abort(); return; }
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { error = new { code, message } }, context.RequestAborted);
    }
}
