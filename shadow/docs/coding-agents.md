# Coding agents and MCP in Shadow Studio

Shadow Studio has an integrated coding workspace and a reusable .NET agent runtime.
The agent sees the same DXF source, immutable shadow models, editor transactions,
and UI state as the rest of Studio. Both the local ASP.NET sample and the static
Blazor application use the same C# controller and tool contracts.

The two independently packable .NET 8 projects are:

| Package | Responsibility |
| --- | --- |
| [`netDxf.Shadow.Mcp`](../src/netDxf.Shadow.Mcp/README.md) | Tool registry and JSON schemas; native DXF and shadow model access; editing, graph operations, snapshots and history; official MCP server transports, resources and prompts |
| [`netDxf.Shadow.Agents`](../src/netDxf.Shadow.Agents/README.md) | Official provider adapters; streaming conversation loop; permissions; cancellation and recovery; context management, compaction and checkpoints; versioned memory/native files and explicit process execution |

The release catalog includes both packages and validates their dependencies,
documentation, source identity, symbols and installed-package execution. They are
published through the repository's ordinary NuGet release workflow. A development
commit or Pages deployment does not itself publish a new NuGet version.

## Use the browser workspace

Open [Shadow Studio](https://wieslawsoltes.github.io/netDxf/) and select **Agents** in
the drawing bar, or press **Ctrl+Shift+A**. The dock has five tabs:

| Tab | What it does |
| --- | --- |
| Conversation | Streaming assistant output, task plan, expandable tool arguments/results, individual approvals, stop and resume, checkpoint import/export |
| MCP | Searchable tool catalog, complete input schemas and an executable JSON tool console using the session's permission policy |
| Files | Versioned text editor, code-block-to-file actions, import/download, compare-and-swap saves and context pins |
| Context | Estimated prompt usage, actual provider usage when supplied, pinned requirements and manual compaction |
| Settings | Provider, model, API credential, optional endpoint, permissions, budgets, ChatGPT sign-in and companion connections |

Choose a provider, enter a model that supports tool calling and enter the full API
key. Keys are retained in memory for the current page session. Only nonsecret
preferences are saved in browser storage. **Disconnect and clear credential**
stops the active operation and removes runtime credentials while retaining the
conversation and in-memory files. Reloading the page starts a new runtime; export
a checkpoint to resume the conversation later.

Model IDs, model context size, output limit and maximum turns are editable. The
runtime uses the official `OpenAI`, `Anthropic`, and `Google.GenAI` .NET packages
through `Microsoft.Extensions.AI.IChatClient`. Endpoint overrides receive the key
and conversation and must support the selected provider protocol. An endpoint's
CORS policy and the browser's local-network permissions still apply.

OpenAI and Google execute their official clients directly in WebAssembly. The
current official Anthropic SDK initializes a native-only HTTP decompression
handler, so browser Anthropic sessions use the companion's official SDK through
a typed streaming bridge. Connect **Local tools & external MCP** before configuring
Anthropic; `--workspace` is optional for this provider bridge. The conversation
loop, tools, approvals and context management continue to execute in the browser.
The local ASP.NET Studio uses the Anthropic SDK directly.

### Permissions

| Mode | Execution policy |
| --- | --- |
| Read only | Only registered read tools are exposed to inference. Direct attempts to invoke a write are rejected as well. |
| Approve changes | Reads run immediately. Each write displays its tool name and exact arguments and waits for an individual decision. |
| Full access | Every registered tool can execute without an individual confirmation. This includes native process tools if the companion was started with that capability. |

Full access changes the harness policy. Available capabilities still come from
the host. A static browser can edit the live drawing and its memory workspace;
native files, compilers, Git and processes are attached explicitly through the
companion below.

Example tasks:

- "Inspect all layers, layouts, block references and entity types. Find orphaned
  references and report any capture or geometry diagnostics."
- "Create a dimensioned plan with separate walls, doors and annotation layers.
  Use one transaction for each logical construction step."
- "Inspect the selected entities and their raw DXF properties, make the requested
  edit, then verify the native source and undo history."
- "Read the connected project's source, implement the drawing exporter, run its
  tests, and show the resulting diff."

## Attach native files, builds and Git

From a checkout, with the .NET 8 SDK available:

```sh
dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- \
  --origin https://wieslawsoltes.github.io \
  --workspace /absolute/path/to/project \
  --allow-processes
```

The companion listens on `http://127.0.0.1:5091`. Copy its printed companion access
token into **Settings → Local tools & external MCP**, then select **Connect local
tools**. Use the exact page origin for a local or custom Studio deployment. See
the [companion README](../samples/netDxf.Shadow.AgentHost/README.md) for the port,
configuration directory and capacity options.

`--workspace` attaches text file listing, paged reads, literal search, exact
patching, complete writes and deletes. These tools are named
`companion_workspace_*`, so they remain distinct from the portable in-memory
`workspace_*` files. The companion's native catalog is empty without that grant.
`--allow-processes` additionally registers `companion_workspace_execute`. Its
executable and argument array can run `dotnet`, test runners, formatters, `git`,
or an explicitly selected shell. The agent can build and test the same source
files it edits.

The process runner uses the granted directory as its working directory, executes
one command at a time, drains both output streams, bounds retained output and
terminates the process tree on cancellation or timeout. It does not retry a
command. A directory grant is a file-tool boundary and working-directory choice,
not an operating-system process sandbox: a process runs with the companion user's
ordinary permissions and inherited environment.

File tools reject path traversal and symbolic-link traversal and require the
current file version for writes or deletes. Native versions detect edits made by
other applications, including changed content with an unchanged timestamp. Writes
stage and replace individual files; a failed multi-file import restores files
already written when possible and reports any incomplete rollback. It does not
claim crash-atomic transactions across a whole repository. Binary files are
listed explicitly; text search and text checkpoint export skip them.

## Sign in with ChatGPT

The companion implements the documented Sign in with ChatGPT flow for open-source
and locally hosted apps. GitHub Pages cannot accept the required loopback OAuth
callback, so the callback and subscription tokens belong to the local companion.

1. Start `netDxf.Shadow.AgentHost` with the Studio page's `--origin`. A native
   workspace is optional for sign-in.
2. In Agent settings choose **ChatGPT subscription**. Enter the companion address
   and the separate access token printed by the companion.
3. Select **Sign in with ChatGPT** and complete the official browser consent flow.
4. Select **Check sign-in**, then **Connect ChatGPT companion**.

The implementation uses a stable host identifier, dynamic public-client
registration, PKCE, state and nonce validation, ID-token signature/issuer/audience
validation, scope checks, single-use callbacks, coordinated token refresh and
logout/revocation. OAuth access/refresh/ID tokens stay in companion memory. Only
the nonsecret host/client registration identifiers are persisted. Restarting the
companion requires signing in again while retaining the registration identity.

Eligible subscription requests use the official Responses endpoint with
`store: false` and streaming enabled. The shared request policy converts the
system role to the documented developer role, supplies the tool namespace and
removes unsupported subscription request parameters. ChatGPT plan availability,
eligible models, account limits and consent are determined by OpenAI.

Official references:

- [Sign in with ChatGPT overview](https://developers.openai.com/siwc)
- [ChatGPT plan usage for open-source apps](https://developers.openai.com/siwc/token-sharing-open-source)
- [Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)

## Connect an external MCP client to this drawing

Start the same companion and select **Connect MCP** in the Studio settings. The
current tab registers its tool catalog over an authenticated WebSocket. Configure
an external MCP client with:

```text
Transport: Streamable HTTP
URL: http://127.0.0.1:5091/mcp
Authorization: Bearer <companion access token>
```

The companion uses the official `ModelContextProtocol.AspNetCore` server. The
package also provides official SDK stream/stdio hosting and an in-process JSON-RPC
session. Protocol initialization, request IDs, cancellation, tools, resources and
prompts are handled by the SDK.

External calls are executed by the connected tab's current agent controller, so
the same approval policy and drawing model checks apply. One tab is attached at
a time. Switching drawings closes that binding; connecting again is explicit.
A disconnect or lost response never causes a mutation to be replayed. The bridge
token is sent in the first WebSocket message, never in the URL. The companion
checks the exact Origin, loopback Host and bearer credential.

## Model and UI coverage

The model registry is generic: its coverage follows the public netDxf object
model captured by Shadow rather than a list of supported drawing commands.

| Surface | Tools and behavior |
| --- | --- |
| Native DXF model | `dxf_types`, public constructor/property/field/enum schemas, live `dxf_source_inspect` and canonical `dxf_source_read` |
| Every captured shadow node | `shadow_objects`, paged `shadow_read`, nested JSON Pointer paths, collection pages and long-text ranges |
| Relationships | Incoming/outgoing reference paths, bounded cycle-aware dependency traversal and diagnostics |
| Property edits | Atomic batches, nested structure fields, previews, source validation and current revision/identity checks |
| Structural edits | Entity/object construction, aliases and references, memberships, deletion and immutable graph patches |
| History and views | Undo/redo, retained snapshots, diffs and the dynamically active branch/editor |
| Complete Studio API | `studio_routes` publishes route templates and request-record schemas; `studio_read` handles GETs; `studio_request` dispatches discovered operations |
| Studio interface | Semantic commands, camera, selection, layers, layouts, branches, palettes, ribbon, settings, dialogs and individual public UI controls |

The public-model boundary retains netDxf's own validation, supported construction
and collection rules. Private fields and arbitrary CLR method execution are not
part of the model protocol. Unsupported data shapes return an explicit error.
Graph data remains accessible even when a renderer cannot project a particular
entity. Live source reads and branch reads are distinct and identified in results.

Studio mutation arguments carry `documentId`, `workspaceId`, `viewVersion` and
`revision`. Read `shadow_state` before constructing an edit. A mismatch refuses the
operation so an agent cannot accidentally apply an old edit to a replaced drawing
or a different branch. Native edits use the existing serialized editor and undo
history. UI actions also check the currently active drawing before changing it.
The generic dispatcher does not expose the agent's own credential/lifecycle routes.

## Runtime and recovery

The harness consumes streaming `IChatClient` updates, collects a complete provider
response, then executes its tool calls. The three provider adapters use the
official SDKs and disable their internal retries so retry decisions remain in one
place. Transient transport, rate-limit and service errors have bounded backoff.
A failed or truncated provider stream cannot execute its partial tool calls.

Only contiguous read tools overlap, with a configurable concurrency limit.
Mutations retain model order and execute serially. Duplicate call IDs are
normalized before execution, and completed calls are retained for resume/dedup.
Cancelled tool rounds still record call/result pairs, including uncertain-result
errors when completion could not be confirmed. Resuming continues the retained
conversation instead of submitting the original task twice.

The UI shows streaming text, tool arguments and results, elapsed time, approval
state, retry status, provider usage, plans and errors. Text is rendered without
executing generated HTML or JavaScript. Protocol events are sequence-numbered and
bounded; polling catches up with pagination and resets correctly after replacing
a session. UI transport recovery retries status reads and never replays a start
or a mutation.

The native provider bridge transmits typed `IChatClient` messages, tool schemas
and streaming updates. It cannot execute tools. It requires the companion bearer
token and exact allowed Origin; the provider key is sent only to the explicitly
connected companion and the selected provider endpoint. Replacing the companion
recreates the Anthropic bridge; disconnecting it disables that provider until it
is connected again.

## Context, compaction and persistence

Context accounting includes system instructions, the current task, pinned
requirements, summaries, retained messages and tool schemas. The UTF-8 estimate is
conservative and calibrated upward using actual input usage supplied by providers.
Set the context window to match the selected model; the counter is an estimate,
not a provider tokenizer guarantee.

Automatic compaction begins before the input budget is exhausted. A compaction
boundary never splits a function call from its result. Recent messages, active
task and pins remain available. Before removing a prefix, the runtime archives
its exact portable representation to `agent-history/` and asks the provider for a
bounded summary. If that summary request fails, a deterministic excerpt summary
is used. If the archive cannot be stored, history is not silently dropped.

Long tool output is written to `agent-results/`; the model receives a bounded
preview and a file path for paged retrieval. Workspace budgets cap file counts and
total bytes. Export and prune unused archives if the session fills its workspace.

Conversation export contains the schema version, session identity, active task,
summary, model messages and tool results, pins, plan and portable text files. It
also preserves ordered provider reasoning/signature metadata required for valid
continuation. Credentials and runtime configuration are excluded. Import validates
all content and file paths before replacing the session and issues new file
versions, so an old editor receipt cannot overwrite an imported file.

The checkpoint describes the agent conversation and its portable workspace. Save
the DXF separately to persist drawing changes. After a reload or in a new runtime,
reconnect native workspace grants and configure credentials to resume an imported
checkpoint. Importing into an existing idle runtime retains its current credentials,
permission setting and companion grants. Manual MCP console
activity is a bounded live event view; it is not synthesized into the model's
conversation history.

## Build and verify

Both NuGet libraries target .NET 8; the static Studio targets .NET 10 WebAssembly.
The ASP.NET Studio and companion target .NET 8.

```sh
dotnet build shadow/src/netDxf.Shadow.Mcp -c Release
dotnet build shadow/src/netDxf.Shadow.Agents -c Release
dotnet run --project shadow/tests/netDxf.Shadow.Mcp.Verification -c Release
dotnet run --project shadow/tests/netDxf.Shadow.Agents.Verification -c Release
dotnet run --project shadow/tests/netDxf.Shadow.AgentNative.Verification -c Release
dotnet run --project shadow/tests/netDxf.Shadow.AgentProviders.Verification -c Release
dotnet run --project shadow/tests/netDxf.Shadow.AgentStudio.Verification -c Release
dotnet run --project shadow/samples/netDxf.Shadow.AgentHost/Verification -c Release
```

After publishing the static application and building the companion in Release:

```sh
python shadow/tests/blazor_agents_browser.py \
  --published artifacts/shadow-studio-browser/wwwroot
```

This browser verification runs the published agent inside Chromium at both the
site root and `/netDxf/`, with OpenAI/Google SDKs in WebAssembly and the Anthropic
SDK in a real native companion. Only provider HTTP responses are scripted; model
edits, source validation, native and portable file operations, process execution,
permissions, checkpoints and UI are real. Native tests exercise actual temporary files,
processes, HTTP, WebSocket and official MCP connections. Tests do not make paid
inference calls or sign in to a real user's ChatGPT account.

The ordinary release pipeline packs all fourteen repository packages and consumes
the exact candidate assemblies in an isolated NuGet feed. GitHub Pages runs the
agent browser verification alongside the existing CAD and layout regressions
before deploying the application.
