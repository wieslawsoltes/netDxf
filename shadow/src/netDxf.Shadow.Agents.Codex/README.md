# Codex app-server client

This .NET 8 library connects a native host to the official `codex app-server` JSONL protocol. Codex owns its agent loop, command execution, conversation state and managed ChatGPT login. This is separate from the `IChatClient` inference adapters in `netDxf.Shadow.Agents`; wrapping the full Codex agent as another inference call would duplicate tool execution responsibilities.

The client supplies the initialization handshake, correlated bidirectional requests, ordered notifications, and convenience methods for account status, browser/device login, model discovery, thread creation/resume, turns and interruption. `CallAsync` exposes additional version-dependent protocol methods. Responses and notifications retain their JSON fields so hosts can render token usage, tool activity and future item types without a library update. Models must be selected explicitly when creating a thread.

```csharp
await using var codex = await CodexAppServerClient.StartAsync(new()
{
    Executable = "/path/to/codex",
    WorkingDirectory = "/path/to/workspace",
});
codex.Notification += notification => HandleNotification(notification);
codex.RequestHandler = (request, token) => AskUserOrExecuteApprovedTool(request, token);
await codex.InitializeAsync("my_local_app", "My local app", "1.0.0");
var account = await codex.ReadAccountAsync();
// Only after the user chooses sign-in:
var login = await codex.BeginChatGptLoginAsync();
// Show authUrl and wait for account/login/completed; Codex owns the callback.
var thread = await codex.StartThreadAsync(selectedModel, "/path/to/workspace");
await codex.StartTurnAsync(thread.GetProperty("thread").GetProperty("id").GetString()!, prompt);
```

The library never reads a local token file, implements OAuth itself, starts sign-in automatically, logs stderr, or saves credentials. The launched process inherits only common runtime/path settings. Trusted hosts may supply explicit child-only `Environment` entries, including transient MCP credentials. Keep executable paths, arguments and environment outside browser-controlled request parameters. Codex itself uses its configured account store; an explicit `CodexHome` selects a separate configuration location. The caller chooses when to start the process and request account/login operations.

For a host's HTTP MCP endpoint, documented startup overrides can set `mcp_servers.shadow_studio.url` and `mcp_servers.shadow_studio.bearer_token_env_var`. Pass the token only through the named child environment variable; avoid secrets in command arguments or persisted configuration. This package does not claim support for an undocumented per-thread MCP configuration shape. Dynamic tools require explicit `experimentalApi` opt-in and a host request handler.

`RequestHandler` is required for approvals, user questions and dynamic tool calls. Its absence returns method-not-found and never grants access. Handlers run independently of the reader so a pending question does not block interruption. Honor the cancellation token: `serverRequest/resolved` cancels stale prompts. Command/file approval responses use the documented decision object. The host must validate an actual user's decision and offered options before replying. Unsupported request methods must be rejected, not silently approved.

`turn/start` acknowledges work; only `turn/completed` with status `completed` establishes success. Cancellation of a `CallAsync` token stops waiting locally and does not cancel a remote turn: send `turn/interrupt` with its thread/turn IDs. Requests are never automatically retried. EOF, malformed/oversized frames and duplicate server request IDs fail the connection; callers must inspect state before reconnecting or repeating mutations. Disposal terminates only the child owned by this client. Use injected stdout/stdin streams for other hosts or deterministic fixtures.

The official app-server interface is experimental; validate against the installed Codex version before deployment. Managed app-server authentication is documented for local/open-source integrations; hosted/commercial integrations use the separately supported Sign in with ChatGPT flow. No live Codex process, login, credential file or provider inference is needed by the verification executable: it uses local protocol fixtures and its own child executable.

References: [official app-server protocol](https://learn.chatgpt.com/docs/app-server), [Codex MCP configuration](https://learn.chatgpt.com/docs/extend/mcp), [one-off configuration overrides](https://learn.chatgpt.com/docs/config-file/config-advanced), [Sign in with ChatGPT app-server configuration](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server).
