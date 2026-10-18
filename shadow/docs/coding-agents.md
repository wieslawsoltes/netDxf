# Coding agents and MCP in Shadow Studio

Shadow Studio has an integrated coding workspace and a reusable .NET agent runtime.
The agent sees the same DXF source, immutable shadow models, editor transactions,
and UI state as the rest of Studio. Both the local ASP.NET sample and the static
Blazor application use the same C# controller and tool contracts.

The GitHub Pages app runs the complete agent loop, all three API-key providers,
coding workspace, JavaScript execution, context management and drawing tools in
the browser. No local companion is required for this workflow. Browser MCP also
exposes the live Studio through MessagePort and, in compatible browsers, WebMCP.

The two independently packable .NET 8 projects are:

| Package | Responsibility |
| --- | --- |
| [`netDxf.Shadow.Mcp`](../src/netDxf.Shadow.Mcp/README.md) | Tool registry and JSON schemas; native DXF and shadow model access; editing, graph operations, snapshots and history; official MCP server transports, resources and prompts |
| [`netDxf.Shadow.Agents`](../src/netDxf.Shadow.Agents/README.md) | Official provider adapters; streaming conversation loop; permissions; cancellation and recovery; context management, compaction and checkpoints; transactional files, isolated code-runner contracts and optional native process execution |

The release catalog includes both packages and validates their dependencies,
documentation, source identity, symbols and installed-package execution. They are
published through the repository's ordinary NuGet release workflow. A development
commit or Pages deployment does not itself publish a new NuGet version.

## Use the browser workspace

Open [Shadow Studio](https://wieslawsoltes.github.io/netDxf/) and select **Agents** in
the drawing bar, or press **Ctrl+Shift+A**. The dock has five tabs:

| Tab | What it does |
| --- | --- |
| Conversation | Normal/Plan/Goal modes, persistent run progress and completion evidence, streaming output, task plan, tool results, approvals, stop and resume, checkpoint import/export |
| MCP | Searchable tool catalog, complete input schemas, JSON tool console, WebMCP registration and browser MessagePort connections using the session's permission policy |
| Files | Versioned text editor, inline diffs, code execution, multi-file/folder/ZIP import, ZIP/bundle download and reviewed folder publishing |
| Context | Estimated prompt usage, actual provider usage when supplied, pinned requirements, compaction and IndexedDB recovery after reload |
| Settings | Provider, model, API credential, optional endpoint, permissions, budgets, ChatGPT sign-in and companion connections |

Choose a provider, enter a model that supports tool calling and enter the full API
key. Keys are retained in memory for the current page session. Browser storage
holds nonsecret preferences and credential-redacted recovery checkpoints. **Disconnect and clear credential**
stops the active operation and removes runtime credentials while retaining the
conversation and files. Reloading starts a new runtime: restore a saved workspace
from **Context → Browser recovery** and reconnect a provider to continue. Download
a conversation checkpoint for a portable backup.

Model IDs, model context size, output limit and maximum turns are editable. The
runtime uses the official `OpenAI`, `Anthropic`, and `Google.GenAI` .NET packages
through `Microsoft.Extensions.AI.IChatClient`. Endpoint overrides receive the key
and conversation and must support the selected provider protocol. An endpoint's
CORS policy and the browser's local-network permissions still apply.

### Normal, Plan and Goal

Choose an execution mode beside the message composer. **Apply mode** changes the
idle session immediately, including the policy used by manual and external MCP
calls; it works without a provider key. **Send task** also applies a pending mode
or success-criteria change before starting. Mode controls are disabled while the
agent is working.

| Execution mode | Behavior | Completion |
| --- | --- | --- |
| **Normal** | Inspect, implement and verify the requested task with the selected tool permissions. | The agent explicitly reports completion and finishes any published plan steps. |
| **Plan** | Investigate with read-only tools and publish a concrete plan. Drawing edits, file mutations, code execution and native commands remain blocked, including with Full access. | A published plan is ready for review; its implementation steps may remain pending. **Execute plan** explicitly switches to Normal and continues the same task. |
| **Goal** | Work toward the entered **Goal success criteria**, using the task itself when that field is empty. Build a plan, perform the work and verify the result. | A nonempty plan has every step completed, and the agent supplies verification alongside its completion report. |

The **Agent run progress** card retains the outcome after a run stops. It shows
model turns, total turns across resumptions, automatic continuations, output-limit
counts, exact pause or blocker details, and an expandable outcome. Verification
is labeled **Agent-reported verification**; the underlying tool receipts remain
available in Conversation for inspection.

All modes continue across ordinary model responses until the agent reports a
completed outcome or a concrete blocker. A response reaching its output-token
limit triggers continuation with retained context; it does not count as task
completion. Incomplete function calls are never executed. The run can also stop
for cancellation, an inference error, a configured budget or lack of progress.
**Resume task** continues its retained conversation and completed tool results.
Resolve a reported blocker before resuming it. A completed Normal or Goal run
does not display Resume; send a new task to request further work.

**Settings → Limits & context budget** uses `0` for **Max model turns** by
default: there is no fixed turn cutoff. A positive value from 1 to 10,000 creates
a resumable per-invocation budget. The separate no-progress budget defaults to
eight turns and is configurable from 2 to 100. **Output tokens / turn** limits
each response, while automatic context compaction preserves longer tasks.
**Use no fixed turn limit** resets an existing turn cap without clearing the
conversation or reconnecting credentials.

Legacy unversioned preferences with the old default of 40 turns migrate once to
zero. Those records cannot distinguish a deliberately selected 40 from the old
default; set 40 again to retain that specific cap. Other custom positive caps are
preserved, and the composer displays the active cap. Versioned preferences retain
an explicitly saved 40. Task criteria are excluded from general preferences;
checkpoints contain the retained run state alongside the task and transcript.
Restoring into a fresh browser runtime stages the saved mode and criteria in the
composer for review. Connect a provider, send a task or use **Apply mode** to apply
that selection. Restoring into an already configured runtime retains its current
mode and criteria.

OpenAI, Anthropic and Google execute directly in WebAssembly. The official
Anthropic SDK's default desktop transport configures native decompression; its
browser adapter instead implements the SDK's public client transport interfaces
over Fetch. The official message services, request models, SSE parser, tool-call
conversion and thinking signatures remain in use. The adapter sends Anthropic's
direct-browser-access header. No private SDK state or unofficial inference SDK
is used. All browser transports omit cookies and stream responses as they arrive.

### Permissions

| Mode | Execution policy |
| --- | --- |
| Read only | Only registered read tools are exposed to inference. Direct attempts to invoke a write are rejected as well. |
| Approve changes | Reads run immediately. Each write displays its tool name and exact arguments and waits for an individual decision. |
| Full access | Every registered tool can execute without an individual confirmation. This includes native process tools if the companion was started with that capability. |

Full access changes the harness policy. Available capabilities still come from
the host. The browser can edit the live drawing and coding workspace, run
JavaScript/WebAssembly, and exchange files through browser-selected folders.
Native `dotnet`, Git executables and operating-system processes use the optional
companion below. **Apply permissions** changes MCP permissions without requiring
a provider key, so an external browser agent can operate Studio directly.

Example tasks:

- "Inspect all layers, layouts, block references and entity types. Find orphaned
  references and report any capture or geometry diagnostics."
- "Create a dimensioned plan with separate walls, doors and annotation layers.
  Use one transaction for each logical construction step."
- "Inspect the selected entities and their raw DXF properties, make the requested
  edit, then verify the native source and undo history."
- "Read the connected project's source, implement the drawing exporter, run its
  tests, and show the resulting diff."

## Browser files, execution and recovery

**Files → Project import, export & folder access** accepts individual UTF-8 files,
whole folders, ZIP archives and the portable JSON workspace bundle. Imports show
per-file diffs and selections before applying a single atomic transaction. Every
file version is checked before any edit commits; a conflict or capacity error
leaves the workspace unchanged. Omitted paths remain intact. Binary files and
generated directories are skipped with a visible report. The portable workspace
holds up to 512 files and 16 MiB of UTF-8 content.

Where the browser provides File System Access, **Connect folder** uses an explicit
user-selected handle. **Review folder writes** compares current disk content with
the workspace and highlights external edits. The browser requests write permission
when publishing; each file is checked again before saving. A multi-file failure
reports the files already saved. Folder publishing is atomic per file, not across
the entire filesystem. Folder handles are not silently reacquired after reload.
Other browsers use folder upload and ZIP download without a companion.

`workspace_runtime` describes the execution contract. `workspace_run_javascript`
accepts either a workspace `path` or inline `code`, optional string `arguments`,
and `timeoutSeconds` from 1 to 120. The code is an async function body, with
`workspace.list/read/write/delete`, `args`, `console`, `assert`, and the browser's
WebAssembly API. For example, save this as `scripts/sum.js` and choose **Run
JavaScript**, or invoke the tool with `{ "path": "scripts/sum.js" }`:

```javascript
const values = JSON.parse(workspace.read('input.json'));
assert(Array.isArray(values), 'Expected an array');
const total = values.reduce((sum, value) => sum + value, 0);
workspace.write('result.json', JSON.stringify({ total }, null, 2));
console.log('Total:', total);
return { total };
```

With **Approve changes**, approve the requested execution in **Conversation**.
The **Stop** control remains available there while code is running.

Each run uses a fresh worker in an opaque-origin frame. It receives a bounded file
snapshot and arguments, with no Studio DOM, provider credentials, browser storage,
or network capability. Logs and returned values are bounded. The harness commits
staged file changes only after successful completion and version validation.
Exceptions, assertion failures, cancellation, timeouts and conflicts discard the
staged edits. The worker can be terminated even during a synchronous infinite
loop. It does not emulate Node.js, a package manager, a native .NET SDK or an OS
shell. Drawing work continues through the real DXF/Shadow/Studio MCP tools.

IndexedDB autosave coalesces changes and saves consistent checkpoints when the
agent becomes idle. A saved copy includes transcript, protected continuation
metadata, task, plan, pins, files and unsaved editor draft. It excludes provider
configuration and credentials. **Save checkpoint now**, **Restore saved workspace**,
**Forget selected saved copy**, and **Keep browser storage** expose recovery and
storage controls. Restoring forks a saved slot so separate tabs do not overwrite
one another. Up to ten copies are retained within 64 MiB; each checkpoint and draft
is bounded to 20 MiB. Quota/blocked-storage errors are visible. Browser eviction
remains possible, so ZIP and conversation downloads provide portable backups.

## Attach native files, builds and Git

From a checkout, with the .NET 8 SDK available:

```sh
dotnet run --project shadow/samples/netDxf.Shadow.AgentHost -- \
  --origin https://wieslawsoltes.github.io \
  --workspace /absolute/path/to/project \
  --allow-processes
```

The companion listens on `http://127.0.0.1:5091`. Copy its printed companion access
token into **Settings → Optional native tools & desktop MCP**, then select **Connect local
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

For a companion-free ChatGPT agent, enable **MCP → Enable browser MCP** and open
Studio in a ChatGPT browser that supports Site tools. The already signed-in
ChatGPT agent can use Studio's WebMCP tools with the page's current permissions.
This connects the external ChatGPT agent to the live page; it does not transfer
subscription credentials into Studio's embedded provider session. Availability
depends on OpenAI's supported browser, model and workspace policy; see the
[official Site tools guide](https://learn.chatgpt.com/docs/webmcp) and the
[browser MCP integration guide](browser-mcp.md).

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

### In the browser

Select **MCP → Enable browser MCP**. A compatible browser receives registered
WebMCP tools through `document.modelContext` (with legacy capability detection).
Same-page integrations can obtain an explicitly granted MessagePort:

```javascript
const connection = await shadowStudioBrowserMcp.createConnection();
// Use connection.port with MessagePortMcpTransport and an MCP client,
// or exchange MCP JSON-RPC initialize, tools/list and tools/call messages.
// Keep the connection alive only while the integration needs this drawing.
await connection.close();
```

The port reaches the official .NET MCP session in WebAssembly. Initialization,
resources, prompts, tool schemas and cancellation use the same SDK. Connections
are bound to one drawing and have bounded messages, requests and retained receipts.
Reusing an identical request ID returns the previous receipt; changing the body
under an existing ID is rejected. Disabling browser MCP or changing drawings
revokes the page capability and closes connections. No HTTP listener, WebSocket
relay or localhost service is involved. See [browser-mcp.md](browser-mcp.md) for
the transport contract and WebMCP availability.

### Native desktop clients

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
state, retry status, provider usage, plans, persistent run state and errors. The
session owns the reserved `agent_plan` and `agent_finish` controls so plans are
validated for the current task and a prose response cannot silently end
unfinished work. Completion is rejected while implementation plan steps are
still pending; Plan and Goal add the mode-specific requirements described above.
The same control can report a concrete blocker while retaining a resumable task.
Text is rendered without
executing generated HTML or JavaScript. Protocol events are sequence-numbered and
bounded; polling catches up with pagination and resets correctly after replacing
a session. UI transport recovery retries status reads and never replays a start
or a mutation.

The optional native provider bridge transmits typed `IChatClient` messages, tool schemas
and streaming updates. It cannot execute tools. It requires the companion bearer
token and exact allowed Origin; the provider key is sent only to the explicitly
connected companion and the selected provider endpoint. It remains available to
embedders of `CreateBrowserBridge`; the Studio's API-key provider connections run
directly and are independent of attaching or detaching native tools.

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
summary, model messages and tool results, pins, plan, portable text files and run
progress, including mode, criteria, interruption details and verification. It
also preserves ordered provider reasoning/signature metadata required for valid
continuation. Credentials and execution authority are excluded. Import validates
all content and file paths before replacing the session and issues new file
versions, so an old editor receipt cannot overwrite an imported file.

Tool-call receipts survive compaction and checkpoint reload, including uncertain
attempts reserved before dispatch and retained after cancellation. Each call ID
is bound to a SHA-256 digest of the tool name and canonical arguments: repeating
the same call returns its receipt, while changed arguments under that ID are
rejected. Recent results remain available; after their bodies leave active
context, a repeat returns historical success or failed/uncertain status and asks
the agent to inspect current state. It does not provide fresh verification. The
ledger allows at most 16,384 receipts within a conservative 4 MiB budget; reaching
either limit rejects new calls before dispatch instead of evicting old IDs.
Legacy schema-1 checkpoints without the optional ledger remain accepted and
rebuild receipts from their retained completed call/result pairs.

The checkpoint describes the agent conversation and its portable workspace. Save
the DXF separately to persist drawing changes. After a reload or in a new runtime,
reconnect native workspace grants and configure credentials to resume an imported
checkpoint. Importing into an existing idle runtime retains its current credentials,
permission setting, selected execution mode/criteria and companion grants. A
mode or criteria change makes retained work resumable under the explicitly
selected configuration rather than restoring an older execution policy. Manual MCP console
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

After publishing the static application:

```sh
python shadow/tests/blazor_agents_browser.py \
  --published artifacts/shadow-studio-browser/wwwroot
```

This browser verification runs the published agent inside Chromium at both the
site root and `/netDxf/`, with all three official provider SDKs in WebAssembly and
no companion process. Only paid provider HTTP responses are scripted; model
edits, source validation, browser code, MessagePort MCP, file uploads, ZIP downloads,
IndexedDB persistence, reload recovery, permissions and UI are real. Pass
`--with-companion` after building AgentHost in Release to additionally verify its
optional native file/process tools. Native tests exercise actual temporary files,
processes, HTTP, WebSocket and official MCP connections. Tests do not make paid
inference calls or sign in to a real user's ChatGPT account.

The ordinary release pipeline packs all fourteen repository packages and consumes
the exact candidate assemblies in an isolated NuGet feed. GitHub Pages runs the
agent browser verification alongside the existing CAD and layout regressions
before deploying the application.
