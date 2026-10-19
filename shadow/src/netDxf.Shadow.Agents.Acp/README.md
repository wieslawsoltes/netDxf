# Agent Client Protocol client

This reusable .NET library connects to a **local ACP coding agent** over newline-delimited UTF-8 JSON-RPC on stdin/stdout. It implements the current stable **ACP v1** protocol. The official documentation labels v2 a draft; v2 is rejected during initialization rather than being interpreted as v1. No provider SDK, API key, Studio instance, MCP server, or `IChatClient` is required by this library.

ACP connects an editor/client to an agent that owns its conversation, inference and tool execution. MCP connects that agent to tools. They are separate protocols: tool-call updates received through ACP are reports, never requests to execute the same tool again in `AgentSession`.

## Connect

```csharp
using System.Text.Json;
using netDxf.Shadow.Agents.Acp;

var services = new AcpClientServices
{
    Notification = (method, parameters) => PresentAgentEvent(method, parameters),
    RequestPermissionAsync = async (request, cancellationToken) =>
        await ShowPermissionOptionsAsync(request, cancellationToken)
        // Return an offered optionId, or null for cancelled. Never infer consent from the tool title.
};
await using var client = await AcpClient.StartAsync(
    new AcpProcessOptions(absoluteExecutable, absoluteWorkspace, configuredArguments), services);

// Display client.Initialization.agentInfo and authMethods. Authenticate only after the user chooses a method.
var session = await client.RequestAsync("session/new", JsonSerializer.SerializeToElement(new
{
    cwd = absoluteWorkspace,
    mcpServers = Array.Empty<object>()
}));
var sessionId = session.GetProperty("sessionId").GetString();
var result = await client.RequestAsync("session/prompt", JsonSerializer.SerializeToElement(new
{
    sessionId,
    prompt = new[] { new { type = "text", text = "Inspect the current drawing using its MCP tools." } }
}), cancellationToken);
```

`AcpClient` also accepts caller-owned readable/writable streams and exposes `InitializeAsync`, which supports transport fixtures and custom transport hosting. `Completion` ends when the read transport closes; `IsConnected` exposes current liveness and `ConnectionError` retains the first transport failure. An unexpected exit before initialization reports EOF rather than an apparent request cancellation. JSON-RPC failures preserve the code, message and optional data in `AcpException`.

## Supported protocol surfaces

| Surface | Behavior |
| --- | --- |
| Initialization | Requests version 1, exchanges client/agent capabilities and implementation metadata, preserves auth methods and extension metadata |
| Authentication | Calls `authenticate` for an advertised agent-managed method; capability-gated `logout`; authentication-required errors are returned to the host |
| Sessions | `session/new`, capability-gated `load`, `resume`, `list`, `close`, `delete`; opaque IDs and pagination cursors |
| Prompting | Rich content capability checks, one active prompt per session, final `stopReason` result and streamed `session/update` notifications |
| Configuration | Advertised legacy session modes and select/grouped-select/boolean config options; refreshed options from response or notification |
| Permission | Asynchronous host callback; only an offered option ID is accepted; missing handler, cancellation or timeout returns the cancelled outcome |
| Cancellation | `session/cancel` plus immediate pending-permission cancellation; waits for the original prompt response before reusing the session; closes transport if cancellation acknowledgement times out |
| Optional editor files | `fs/read_text_file` and `fs/write_text_file` callbacks, each advertised independently |
| Optional terminals | Complete create/output/wait_for_exit/kill/release callback surface, advertised only when supplied |
| Optional elicitation | Separate form and URL callbacks/capabilities; returns accept/decline/cancel; forwards `elicitation/complete` |
| General JSON-RPC cancellation | Sends and receives `$/cancel_request`; responses, requests and notifications are dispatched independently |
| Streamed content | Forwards original text, tool creation/patches, diff/content, plan, mode, command, usage, notice and unknown extension notifications |

Optional methods are called only when advertised. Omitting a client callback disables its corresponding capability. This library does not advertise interactive-terminal authentication or the optional compaction-placement extension. An agent needing a separate visible terminal login must be authenticated through its documented CLI workflow, then reconnected; it must not be sent to `authenticate` as if it were an agent-managed method.

The client does not aggregate tool patches or render rich content. Hosts retain/render the raw update records and respect the protocol's patch semantics. File and terminal callbacks must implement their entire advertised contract and enforce the host's workspace, process and approval policy. Form elicitation hosts render and validate the supplied schema; URL elicitation hosts present an untrusted URL for user interaction. This library never automatically launches a URL, terminal login, tool, or shell command from an inbound request.

## Companion hosting boundary

Configure the installed executable, argument array, workspace and optional environment **at companion startup**. Do not accept executable paths or process arguments from browser requests. The executable and working directory must be absolute. `ProcessStartInfo.ArgumentList` and `UseShellExecute = false` keep argument boundaries intact; there is no shell concatenation or automatic package installation.

Only ordinary runtime/location environment variables are inherited (PATH, HOME, OS and XDG locations, temporary directories, locale, `DOTNET_ROOT` and architecture-specific variants, and `DOTNET_ROLL_FORWARD`). Preserving runtime locations allows framework-dependent agents to use an explicitly installed .NET runtime, including setup-dotnet installations on CI. Provider keys and companion bridge tokens are not automatically copied. A host may explicitly supply environment entries in `AcpProcessOptions.Environment`; they must not be serialized into browser status, transcripts or logs. Agent-owned authentication storage remains owned by the configured agent: this client does not read or convert credential files. Connection limits, arguments and explicit environment entries are validated before starting a child process.

For Studio tools, the host may supply an authorized Studio MCP server in `session/new`/`load`/`resume`. ACP v1 stdio MCP entries contain `name`, absolute `command`, `args` and `env` and **omit `type`**. HTTP/SSE entries use a `type` field and require the agent to advertise the corresponding MCP capability. The companion remains responsible for authorization and scope of that MCP endpoint.

The transport bounds message bytes and pending/incoming requests, separates stderr from protocol stdout, and shuts down owned subprocesses on disposal. Stderr is drained and discarded unless an explicit observer is supplied; such logs can contain sensitive data. Host event callbacks should enqueue promptly rather than block the protocol reader.

## Studio client

Studio's **Agents → Local agents** panel uses the configured companion profile. It offers only advertised agent-managed login methods, session history/lifecycle actions, and session configuration controls. Model, reasoning, mode and other select or boolean options come from the agent's `configOptions`; legacy modes are the fallback. Loaded message groups and tool patches retain their identities. Permissions present the agent's offered choices, with cancellation available.

The first Studio host exposes text prompts and permission requests. It does not advertise editor file, terminal or elicitation services merely because the reusable library supports them. The Studio host supplies its workspace and HTTP MCP configuration; if an agent lacks HTTP MCP, the panel explains that drawing tools are unavailable for that connection. Interactive terminal authentication remains in the agent's own CLI. Neither connection secrets nor local-agent transcripts are saved by the panel.

## Verification

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Agents.Acp.Verification
```

The verification launches a local fixture executable speaking actual ACP messages. It covers capability negotiation/gates, auth-required errors, session lifecycle, modes/configuration, rich updates, full-duplex file/terminal/permission/elicitation requests, cancellation races, reused sessions, invalid permissions, UTF-8 framing, oversized/malformed messages, concurrent request limits, EOF and environment isolation. Browser contracts are covered by `node --test shadow/tests/browser/external-agent-contracts.test.mjs`. No installed third-party coding agent, API key or live provider inference is required. Passing these tests verifies this protocol implementation; it does not assert compatibility with every release of every ACP agent.

## Protocol references

- [Initialization and version/capability negotiation](https://agentclientprotocol.com/protocol/v1/initialization)
- [Agent-managed and interactive-terminal authentication](https://agentclientprotocol.com/protocol/v1/authentication)
- [Session setup and MCP configuration](https://agentclientprotocol.com/protocol/v1/session-setup)
- [Stable v1 schema, including optional resume/close methods](https://agentclientprotocol.com/protocol/v1/schema)
- [Prompt lifecycle and cancellation](https://agentclientprotocol.com/protocol/v1/prompt-turn)
- [Tool updates and permission outcomes](https://agentclientprotocol.com/protocol/v1/tool-calls)
- [Stdio framing](https://agentclientprotocol.com/protocol/v1/transports)
- [Request cancellation](https://agentclientprotocol.com/protocol/v1/cancellation)
