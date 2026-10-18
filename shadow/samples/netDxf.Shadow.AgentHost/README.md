# netDxf Shadow Agent Host

This optional .NET 8 companion connects a GitHub Pages Studio tab to desktop HTTP MCP clients, supports **Sign in with ChatGPT** for ChatGPT plan usage, and can grant the Studio agent access to a local source directory and native build/test commands. The browser Studio already runs OpenAI, Anthropic and Google inference, persistent coding files, JavaScript execution and browser MCP without this host. The drawing and editor remain in the explicitly connected browser tab.

## Start the companion

From the repository root:

```sh
dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- \
  --origin https://wieslawsoltes.github.io
```

The host listens on `http://127.0.0.1:5091` and prints a random bridge token once. Enter that address and token in the Studio's Agent panel, then connect the companion. The bridge token grants access to this companion instance; keep it in the local agent/MCP configuration. Studio keeps the token in memory.

For native coding work, grant a directory when starting the companion:

```sh
dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- \
  --origin https://wieslawsoltes.github.io \
  --workspace /absolute/path/to/netDxf \
  --allow-processes
```

`--workspace` enables file list/read/search/write/patch/delete tools. `--allow-processes` separately enables `companion_workspace_execute` for builds, tests, compilers, formatters, Git, and shell commands. The process tool accepts an executable and argument array, captures bounded stdout/stderr and exit status, and terminates the process tree when canceled or timed out. Native commands run as your OS account: **the directory grant is the initial working directory, not an OS sandbox for arbitrary processes**. Studio's selected tool permission mode still controls whether the agent can dispatch mutating tools.

| Option | Default | Purpose |
| --- | --- | --- |
| `--port` | `5091` | Port on `127.0.0.1`; public interfaces are never bound. |
| `--origin` | `https://wieslawsoltes.github.io` | Exact allowed browser origin; repeat for another trusted Studio origin. |
| `--state-file` | User local application data, `netDxf/agent-host.json` | Nonsecret persistent host and issued client registration. |
| `--workspace` | Disabled | Existing directory explicitly available to native file tools. |
| `--allow-processes` | Disabled | Native process capability; requires `--workspace`. |
| `--max-files` | `20000` | Maximum enumerated native workspace files. |
| `--max-bytes` | `134217728` | Native workspace byte budget, 128 MiB. |

For a stable bridge token, set `SHADOW_AGENT_BRIDGE_TOKEN` to at least 32 bytes of visible ASCII text before starting the process. A configured token is not printed. A generated token changes when the host restarts. `--help` lists the same options.

The own `http://127.0.0.1:<port>` origin is always allowed. Other origins must be exact HTTPS origins, or loopback HTTP origins for local development. GitHub Pages uses an origin such as `https://wieslawsoltes.github.io`, without the repository path. Browsers may ask to permit access to a local network device before connecting to a loopback companion; that permission is controlled by the browser.

## Anthropic and official SDK inference

All three API-key providers run their official .NET SDKs directly in Studio's WebAssembly runtime. Anthropic uses the SDK's public transport interfaces with browser Fetch while retaining its official message services and streaming parser. Attaching or detaching this companion does not affect those direct provider connections. The typed bridge below remains an optional embedding API for applications that choose a native inference transport; no native directory or process grant is required for it.

The browser's `CreateBrowserBridge` client sends a typed `/providers/stream` request with the bridge bearer token and per-request provider configuration. The companion constructs the official SDK and streams Microsoft's `ChatResponseUpdate` JSON in bounded NDJSON frames. Text, protected thinking signatures, tool calls/results, token usage, cancellation and terminal failures survive the connection; tools are executed by the browser harness. The companion does not retain provider API keys or automatically replay inference. Explicit endpoint overrides are validated as HTTPS or HTTP loopback and passed to the official SDK, matching direct provider configuration. The selected endpoint receives the provider credential and request content.

## ChatGPT plan sign-in

In the Agent panel, configure the companion and choose **Sign in with ChatGPT**. Finish the official authorization in the opened window, then return to Studio and connect the ChatGPT companion. Select a model available to the account. `GET /v1/models` exposes the current account model catalog after sign-in.

The host implements OpenAI's documented public-client flow for open-source local applications. Initial authorization uses the documented `dynamic_agent_client` registration mechanism, an application name and stable `ext_agent_host_id`. The callback returns the issued client ID; subsequent sign-ins reuse that registration. There is no bundled third-party client ID or client secret.

The callback is exactly `http://127.0.0.1:<port>/auth/callback`. OpenAI's local registration flow requires the `127.0.0.1` HTTP callback and stable callback path; a GitHub Pages URL or `localhost` is not substituted. The host starts that listener before authorization begins. It validates one-use OAuth state, PKCE, the signed ID token, trusted issuer, client audience, expiry, nonce and bound account identity before enabling inference.

Access tokens, rotating refresh tokens, ID tokens and PKCE secrets stay in process memory. The registration file contains only the stable host ID, issued client ID and account identity mapping. Framework request logging is disabled so callback codes and query strings are not logged. Restarting the host requires signing in again. Logout clears local credentials, cancels active inference and attempts remote revocation; status reports if revocation could not be confirmed. To use a different account, start a separate host registration with `--state-file`.

The browser uses the official OpenAI Responses SDK through the companion with its **bridge token**. The companion sends the native OAuth access token only to the fixed public endpoint `https://api.openai.com/v1/responses`. It coalesces refresh, never automatically replays an inference request, and forwards SSE terminal failures. Both the SDK adapter and proxy apply the supported ChatGPT contract: streamed responses, `store:false`, complete input history, developer roles, grouped local function tools and removal of unsupported request fields. Hosted MCP execution is not used; the Studio executes its local MCP tools through the harness. Account entitlements, plan usage consent, rate limits and currently supported models remain controlled by OpenAI.

Official references:

- [Sign in with ChatGPT for open-source apps](https://developers.openai.com/siwc/token-sharing-open-source)
- [Sign-in protocol](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [Models and inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [Preview limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)

## External MCP clients

Connect the desired Studio tab to the companion, then configure an MCP client that supports Streamable HTTP:

```json
{
  "url": "http://127.0.0.1:5091/mcp",
  "headers": {
    "Authorization": "Bearer <your-bridge-token>"
  }
}
```

This is the endpoint and header information; place it in the configuration format required by your MCP client. The endpoint uses the official `ModelContextProtocol.AspNetCore` package. Tools are discovered dynamically from the connected browser's registry, including its DXF, shadow, Studio and attached companion workspace tools. The browser remains responsible for mutation approval. A tool's read-only hint is descriptive and does not bypass the browser's executor.

Only one Studio tab can register at a time. A second tab is rejected until the first explicitly disconnects. The tool catalog is captured at registration and is not silently rebound to another tab. Disconnecting completes pending calls with an error that explains that an edit may already have happened; the host never replays it. Clients should list tools again after an explicit reconnect. Canceled external MCP calls send a matching cancellation notification to Studio. The relay permits 32 pending calls and bounds messages to 1 MiB and execution waits to five minutes.

## HTTP and WebSocket contract

All HTTP API and MCP requests require `Authorization: Bearer <bridge-token>`, an exact accepted Host header, a loopback peer and an allowed Origin when an Origin is present. CORS preflight is accepted only for allowed origins. The OAuth callback instead requires its pending OAuth transaction. The browser WebSocket sends its bearer token in the first message rather than a URL or query parameter.

| Route | Contract |
| --- | --- |
| `GET /health` | Connection, tool count, pending call count and native capability status. |
| `POST /auth/login` | `{ "authorizationUrl": "https://auth.openai.com/..." }`. |
| `GET /auth/status` | `{ "authenticated": false, "expiresAt": null, "email": null, "error": null }`. |
| `POST /auth/logout` | Clears the local session; returns `authenticated:false` and confirmed remote `revoked` status. |
| `GET /auth/callback` | Validates the OAuth transaction; returns plain text. |
| `POST /v1/responses` | Normalized ChatGPT Responses JSON; streamed upstream response. |
| `GET /v1/models` | Current signed-in account model catalog from the fixed OpenAI endpoint. |
| `POST /providers/stream` | Typed `ShadowAgentBridgeRequest` → NDJSON update/completed/error frames; native official SDK inference for the browser. |
| `GET /workspace/tools` | Array of `McpToolDefinition`; `[]` when no workspace was granted. |
| `POST /workspace/call` | `{ "name": "companion_workspace_read", "arguments": { "path": "README.md" } }` → `{ "text": "...", "isError": false }`; unavailable without a workspace grant. |
| `/mcp` | Official Streamable HTTP MCP transport. |
| `/studio` | Single active browser WebSocket, described below. |

Browser registration:

```json
{
  "type": "register",
  "token": "<bridge-token>",
  "tools": [
    {
      "name": "studio_state",
      "description": "Inspect the studio state",
      "inputSchema": { "type": "object", "properties": {} },
      "readOnly": true
    }
  ]
}
```

The host responds with `{ "type": "registered", "toolCount": 1 }`. Tool requests are `{ "type": "call", "id": "...", "name": "...", "arguments": {} }`; the browser answers `{ "type": "result", "id": "...", "result": { "text": "...", "isError": false } }`. Cancellation is `{ "type": "cancel", "id": "..." }`. IDs belong to this connection; late results from canceled operations are discarded.

The native catalog prefixes every name with `companion_` so that local disk files cannot be confused with the browser's in-memory `workspace_*` tools. Path-based file operations reject traversal and symbolic links. Build output and `.git`, `bin`, `obj`, `node_modules` directories are excluded from workspace enumeration. Text operations use UTF-8; process commands can invoke external tools for other file formats. Native calls are bounded to 16 in flight and run once, with request cancellation passed to the executor.

## Verification

From the repository root:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.AgentProviders.Verification
dotnet run --project shadow/samples/netDxf.Shadow.AgentHost/Verification
```

Provider checks exercise the real official .NET SDKs against controlled HTTP streams, including function call/result replay, usage, terminal failure detection, provider signature preservation, API error classification, OAuth PKCE, ID-token verification, refresh rotation and revocation. Host checks start temporary loopback listeners and use an official MCP client, real WebSockets, temporary source files and a cancellable child process. They verify authentication, origin and Host rejection, correlation, single-tab binding, cancellation, uncertain mutation outcomes, native grants and version checks. They do not spend provider credits or claim a live account authorization test.

The reusable provider and auth implementation lives in `netDxf.Shadow.Agents`; the reusable MCP implementation lives in `netDxf.Shadow.Mcp`. This executable sample consumes those independently packable projects. The provider package references the official [OpenAI .NET SDK](https://github.com/openai/openai-dotnet), [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp), [Google Gen AI .NET SDK](https://github.com/googleapis/dotnet-genai) and Microsoft.Extensions.AI adapters. The MCP package uses the official [C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk).
