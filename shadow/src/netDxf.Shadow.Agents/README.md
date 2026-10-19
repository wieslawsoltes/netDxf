# netDxf.Shadow.Agents

A standalone .NET 8 coding and CAD agent harness built around `Microsoft.Extensions.AI.IChatClient` and the transport-independent tool contracts in `netDxf.Shadow.Mcp`. It can run in a native application or the browser Studio, and is packed independently as `netDxf.Shadow.Agents`.

The package provides streaming inference, tool execution, explicit permission modes, bounded retries, cancellation, context compaction, pinned context, plans, event polling, portable checkpoints, file workspaces and optional native process execution. The provider factory uses the official OpenAI, Anthropic and Google .NET libraries. Studio's visuals and local companion are consumers of the package.

## Dependencies and provider support

| Component | Package | Pinned version |
| --- | --- | --- |
| OpenAI Responses | `OpenAI` | `2.14.0` |
| Anthropic Messages | `Anthropic` | `12.58.0` |
| Google Gen AI | `Google.GenAI` | `1.26.1` |
| OpenAI `IChatClient` adapter | `Microsoft.Extensions.AI.OpenAI` | `10.11.0` |
| Common chat abstraction | `Microsoft.Extensions.AI.Abstractions` | `10.11.0` |
| Shared tool executor | `netDxf.Shadow.Mcp` | Same repository package version |

| Provider value | Native .NET | Browser Studio |
| --- | --- | --- |
| `openai` | Official Responses SDK and an API key. | Official Responses SDK with a session API key. |
| `google` / `gemini` | Official Google Gen AI SDK and an API key. | Official Google Gen AI SDK with a session API key. |
| `anthropic` | Official Anthropic SDK and an API key. | Official SDK message services and `IChatClient` adapter over the package's browser Fetch transport; no companion. |
| `chatgpt` | Official Responses SDK with a valid Sign in with ChatGPT access token. | Official Responses SDK through the local ChatGPT companion; browser holds only a bridge token. |

All three API-key providers run in WebAssembly without a companion. Provider endpoints must allow the application's origin through CORS. Requests explicitly enable browser response streaming, use API credentials without cookies and never persist a provider key as part of a checkpoint.

The pinned Anthropic SDK's default desktop client eagerly configures `HttpClientHandler.AutomaticDecompression`, which is unavailable on WebAssembly. The browser adapter implements the SDK's public `IAnthropicClient` and `IAnthropicClientWithRawResponse` transport interfaces. The official SDK still supplies `MessageService`, `MessageServiceWithRawResponse`, typed request bodies, SSE parsing, tool conversion, usage accounting, thinking signatures and `AsIChatClient`. The adapter forwards the SDK's serialized body through `HttpClient`/Fetch and sends Anthropic's explicit `anthropic-dangerous-direct-browser-access: true` header. It uses no private reflection, modified provider assembly or separate JavaScript inference library. The provider verifier exercises this public SDK integration whenever the pinned package changes.

`ShadowAgentProviders.Create(options)` selects browser transport automatically on WebAssembly. `ShadowAgentProviders.CreateBrowser(options, httpClient)` makes the same selection explicitly on any runtime, which lets applications validate browser request behavior using an injected `HttpClient`. The caller retains ownership of an injected client. Responses remain streamed, SDK timeouts cover body reads, cancellation disposes the stream, HTTP error bodies are bounded, and `Retry-After` is passed to the harness. An Anthropic turn completes only after `message_stop`; a connection lost after `stop_reason` still fails the turn.

Official sources: [OpenAI .NET](https://github.com/openai/openai-dotnet), [Anthropic C#](https://github.com/anthropics/anthropic-sdk-csharp), [Google Gen AI .NET](https://github.com/googleapis/dotnet-genai), [Microsoft IChatClient](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient).

## Run an agent over DXF, shadow and file tools

The following native example creates an independent in-memory session. Supply your own API key and a tool-capable model ID through the application configuration:

```csharp
using netDxf;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;

var provider = new ShadowAgentProviderOptions
{
    Provider = "openai",
    Model = Environment.GetEnvironmentVariable("OPENAI_MODEL")
        ?? throw new InvalidOperationException("Set OPENAI_MODEL."),
    ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
        ?? throw new InvalidOperationException("Set OPENAI_API_KEY."),
};

var source = new ShadowSession(new DxfDocument());
var editor = new EditorSession(source);
var files = new MemoryAgentWorkspace();
var tools = ShadowMcpTools.Create(new(() => editor, () => source));
AgentWorkspaceTools.Register(tools, files);

using var agent = new AgentSession(
    ShadowAgentProviders.Create(provider),
    tools,
    new AgentOptions
    {
        Model = provider.Model,
        PermissionMode = AgentPermissionMode.ReadOnly,
    },
    workspace: files,
    redactor: text => text.Replace(provider.ApiKey, "[redacted]",
        StringComparison.Ordinal));

agent.EventEmitted += entry =>
{
    if (entry.Type == "assistantDelta") Console.Write(entry.Message);
};

await agent.RunAsync("Inspect the drawing and explain its available editing capabilities.");
Console.WriteLine(agent.Poll().Status);
```

`AgentSession` accepts any `IMcpToolExecutor`, so a host can supply an unrelated code or application tool registry. It also accepts any compatible `IChatClient`, including test clients. It owns the chat client by default; use `ownsClient:false` if a shared caller manages its lifetime. A supplied `HttpClient` passed to `ShadowAgentProviders.Create` or `CreateBrowserBridge` remains caller-owned.

`RunAsync` records runtime failures as an `error` status and event so the UI can recover and continue. Configuration and concurrent-operation errors can throw before execution starts. Inspect `Poll().Status` after the task completes. Run, manual execution, import, reset and manual compaction are mutually exclusive. Cancel and await an active run before disposing the session.

## Persistent runs and execution modes

`AgentOptions.Mode` selects `AgentRunMode.Normal`, `Plan` or `Goal`. It is independent of `PermissionMode`:

| Mode | Execution and completion |
| --- | --- |
| `Normal` | Continues implementation until an explicit completed or blocked outcome. Any published implementation plan must be complete before the task can finish. |
| `Plan` | Advertises read-only tools and rejects mutations at execution time, including manual/MCP calls under `FullAccess`. Planning metadata remains available. Completion requires a published plan; implementation steps may remain pending. |
| `Goal` | Uses `SuccessCriteria` (up to 16,000 characters), or the task when criteria are empty. Completion requires a nonempty completed plan and nonempty reported verification. |

For example:

```csharp
var options = new AgentOptions
{
    Mode = AgentRunMode.Goal,
    SuccessCriteria = "All requested entities exist, drawing units are preserved, and validation reports no errors.",
    PermissionMode = AgentPermissionMode.Ask,
    MaxTurns = 0,
    MaxNoProgressTurns = 8,
};
```

The session owns the reserved `agent_plan` and `agent_finish` control tools in standalone and Studio hosts. `ToolDefinitions` exposes them alongside the host's catalog. The model explicitly reports a completed outcome with its summary and verification, or a blocked outcome with a concrete blocker. Control calls are revalidated for the current task; a reused call identifier cannot restore a previous task's plan or bypass completion requirements. `agent_finish` is a run control: a manual/MCP call outside an active model response cannot declare a task complete. Completion validation does not independently prove the truth of model-supplied evidence; hosts can display its verification beside the actual tool receipts.

A text response alone does not finish a task. The harness continues after normal prose responses and provider output-token limits while retaining the task and protocol-valid history. Truncated function calls are not executed. Providers normalize output-limit signals so this behavior remains consistent across their official clients. Transient inference retries remain separate from task continuation and do not replay completed mutations.

`MaxTurns = 0` means no fixed model-turn cutoff. A positive value from 1 to 10,000 pauses after that many turns in the current invocation; `ResumeAsync` starts another invocation against retained history. `MaxNoProgressTurns` defaults to 8 and accepts 2–100, preventing a stalled agent from continuing indefinitely. Cancellation, errors and explicit blockers also retain recoverable task state. Completed Normal/Goal work stays completed; a host can deliberately change Plan to Normal and resume to execute the reviewed plan.

`RunState` and `Poll().Run` provide `Mode`, `Phase`, current and total turn counts, `TurnLimit`, continuation/output-limit/no-progress counters, `Resumable`, `StopReason`, `Detail`, `SuccessCriteria`, `CompletionSummary` and `Verification`. Use the explicit phase and resumable flag for controls; assistant text and the arrival of a final streaming chunk are not completion signals. Run metadata is checkpointed and retained through compaction. Import preserves the importing host's `Options.Mode` and `Options.SuccessCriteria`; changing them turns a retained task into a paused, resumable task under the current policy.

## Permissions and execution order

`AgentOptions.PermissionMode` is the execution policy for the supplied registry:

| Mode | Behavior |
| --- | --- |
| `ReadOnly` | Advertises read-only tools and rejects mutating calls, including calls invented by a model. |
| `Ask` | Executes reads; emits `approvalRequested` and awaits a decision for each mutation. This is the default. |
| `FullAccess` | Executes tools explicitly registered by the host without per-call approval; Plan mode still blocks mutations. |

For `Ask`, display the `AgentApproval` in the `approvalRequested` event's `Data`. Its JSON fields include `requestId`, `tool`, `arguments` and `expiresAt`. Call `agent.Approve(requestId, true)` or `agent.Approve(requestId, false)` after the host's decision. Approval denial and expiry are returned to the model as tool results. `agent.ExecuteToolAsync(name, arguments, cancellationToken)` uses the same policy for manual/MCP invocations.

Tool mutations run in model order. Only adjacent read-only calls can overlap, up to `MaxParallelReadTools`; a write waits for earlier reads, and later reads wait for that write. The executor must honor cancellation and correctly classify tools. A custom executor that marks a write as read-only changes the capability contract and must enforce any additional host policy itself.

Inference completes before any of its tool calls are dispatched. A failed or truncated stream cannot execute a partially received call. The harness retries only inference with a bounded exponential delay and jitter, observes supported `Retry-After` values, and discards partial attempts. Provider SDK retries and Google automatic continuations are disabled to keep attempts observable and bounded. Authentication, invalid input, permission and account-plan failures require recovery instead of an automatic retry.

Duplicate function-call IDs in one response are removed before execution. Completed call IDs are retained for the active session, and retained transcript results rebuild that cache on import. Mutations are never automatically replayed. When a tool is interrupted, its result explains that an edit may already have committed; inspect fresh document/file state before deciding whether to repeat it.

`MaxToolCallsPerTurn` bounds a provider batch (64 by default). Oversized batches and output-limited tool responses execute no tools and retain no unfinished call records. This lets a host safely request a smaller batch or raise its output budget before continuing.

The harness supplies workflow guidance only for tools the host advertises and the permission policy allows. For drawing work it favors batched entity creation, targeted type discovery, filtered object reads, bulk property transactions and fresh mutation receipts. Geometric topology tools are distinguished from ownership/reference graph tools. Read-only context estimates count only advertised tools. MCP function declarations explicitly disable OpenAI strict normalization so optional fields, arbitrary DXF value schemas and open creation-property maps preserve their native semantics; argument validation remains in the MCP registry. See [OpenAI function calling](https://developers.openai.com/api/docs/guides/function-calling).

The [opt-in live evaluator](../../tests/netDxf.Shadow.Agents.LiveEvaluation/README.md) covers creation, selective edits and topology with native assertions, saved DXF reloads, tool receipts, timing/usage metrics and a shared cost ledger. Its offline `--self-test` needs no provider or credential. Live mode requires an explicit provider/model, environment-only key, operator-verified prices and a budget reservation before each inference.

## Workspace tools and native commands

`IAgentWorkspace` supplies file listing, bounded character reads, complete writes, deletes, literal search, export and import. `AgentWorkspaceTools.Register(registry, workspace)` adds:

| Tool | Behavior |
| --- | --- |
| `workspace_list` | Paths, versions and size metadata. |
| `workspace_read` | Character paging with offset, length, current version and `hasMore`. |
| `workspace_write` | Create with `expectedVersion:0`, or replace using the current version. |
| `workspace_patch` | Replace exactly one occurrence of `oldText`, requiring a current version. |
| `workspace_search` | Bounded literal matches with file paths and line numbers. |
| `workspace_delete` | Delete after checking the current version. |

`MemoryAgentWorkspace` is bounded, uses forward-slash relative paths, validates imports before replacing live data and issues fresh versions on import. Its default budget is 512 files and 16 MiB.

### Atomic files and browser code

`MemoryAgentWorkspace` also implements `IAgentTransactionalWorkspace`. Its `Apply` method validates every path, expected version and the final capacity before committing a batch. A null content deletes an existing file; expected version zero creates an absent file. If any edit conflicts, the entire batch leaves the workspace unchanged:

```csharp
IAgentTransactionalWorkspace files = new MemoryAgentWorkspace();
files.Apply([
    new AgentFileChange("input.json", "[2, 3, 5]", ExpectedVersion: 0),
    new AgentFileChange("sum.js", "return JSON.parse(workspace.read('input.json')).reduce((a,b) => a+b, 0);", ExpectedVersion: 0),
]);
```

`IAgentCodeRunner` is a portable host contract for isolated JavaScript execution. It receives an `AgentCodeRequest` containing source, an immutable file snapshot, arguments, a deadline and output budget. It returns an `AgentCodeResult` with logs, exit status, timing, a JSON value and staged `AgentCodeChange` entries. The host supplies its runtime implementation; the package has no browser UI dependency.

`AgentCodeTools.Register(registry, files, runner)` adds the read-only `workspace_runtime` discovery tool and the mutating `workspace_run_javascript` execution tool. The latter accepts exactly one of inline `code` or a workspace `path`, optional string `arguments`, and `timeoutSeconds` from 1 to 120. It validates the complete runtime receipt before committing successful edits with the original file versions. Errors, cancellation, timeout, malformed receipts and version conflicts commit nothing. These tools use the same agent permission and approval policy as drawing edits.

The browser Studio supplies a dedicated worker in an opaque-origin frame. Code uses an async function body with `await`, `return`, `workspace.read/write/delete/list`, `args`, `console`, `assert`, and `WebAssembly`. The worker receives no Studio objects or provider credentials and cannot access the network or page storage. Stopping a run terminates its worker before another execution starts. See the [browser coding guide](../../docs/coding-agents.md) for UI usage, persistent workspaces, project ZIP import/export and reviewed folder writes.

### Native commands

Native applications can explicitly attach a source directory and process capability:

```csharp
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;

var directory = "/absolute/path/to/project";
var nativeFiles = new DirectoryAgentWorkspace(
    directory, maximumBytes: 128 * 1024 * 1024, maximumFiles: 20000);
var nativeTools = new McpToolRegistry();
AgentWorkspaceTools.Register(nativeTools, nativeFiles);

// Register only when the application/user grants native process execution.
AgentCommandTools.Register(nativeTools, new LocalAgentCommandRunner(directory));
```

`DirectoryAgentWorkspace` checks file content stamps for version conflicts, stages writes before replacing files, rejects path traversal and symbolic links, and bounds enumeration. It skips `.git`, `bin`, `obj` and `node_modules` in enumeration. Text operations use UTF-8; file metadata can identify binary files, while text search skips binary content. Workspace exports contain UTF-8 text files; binary files stay in the native directory and remain visible in file metadata. Prefer a separate memory workspace for the harness's transcript archives when native tools grant a large repository.

The optional `workspace_execute` tool accepts an `AgentCommand` with `Executable`, an `Arguments` array, relative `WorkingDirectory`, and `TimeoutSeconds`. `LocalAgentCommandRunner` uses `ProcessStartInfo.ArgumentList`, runs one process operation at a time, drains bounded stdout/stderr, reports exit code, truncation, timeout and elapsed time, and kills the process tree on cancellation. Shell commands can be run by explicitly selecting a shell executable. Processes run as the hosting OS account; the working-directory grant is not an OS sandbox. Browser-only `FullAccess` does not create a shell.

The [Agent Host sample](../../samples/netDxf.Shadow.AgentHost/README.md) exposes native tools with `companion_` prefixes to distinguish source files on disk from Studio's memory workspace. Startup `--workspace` and `--allow-processes` are separate grants.

## Streaming events and UI state

Subscribe to `EventEmitted` for immediate updates, or use `Poll(afterSequence)` to fetch at most 256 events after a known sequence. `AgentEvent` includes sequence, type, message, optional JSON data and timestamp. The bounded event log retains at most 4,096 entries and approximately 4 MiB of event text/data; `EarliestSequence` and `LatestSequence` let a client recognize a polling gap.

Events cover assistant text deltas and completed messages, turns, usage, retry and continuation notices, tool start/completion with elapsed time and results, approval requests, plans, run snapshots, compaction, cancellation, completion, blockers, pause and errors. Provider usage reports input/output/total/cached tokens when available. `Poll` also returns current status, running state, session ID, estimated context use, configured context limit and `Run` metadata. A UI can update its persistent progress card from `run` snapshots without appending a new transcript message for every counter change. UI observers cannot terminate the inference loop by throwing; callers should marshal events to their UI thread as needed.

`Poll().Usage` contains cumulative successful-inference totals, including compaction, plus cache-write and reasoning counts when reported, elapsed inference time and the most recent first-token latency. Each usage event has its inference delta and a `totals` snapshot, so cursor replay never needs to sum an already-counted request. Unknown provider fields stay null. Failed or interrupted requests can still be billable and are not represented as complete usage; the opt-in evaluator reserves their upper-bound cost separately. Import/reset starts a new observed usage interval.

### Prompt caching and overhead

Tool declarations retain stable ordinal ordering and reuse the same function objects, allowing SDK schema caches and provider prefix caches to reuse them. Context accounting memoizes immutable transcript sizes instead of serializing the entire history on every UI poll. Stable instructions and pinned reference material precede the replaceable compaction summary.

Provider caching is deliberately provider-specific:

- **OpenAI:** the service caches eligible prefixes automatically. The client sends a stable, nonsecret key for its lifetime; `PromptCacheKey` can supply an application key. Earlier models use it for routing, while newer models use it for cache accounting. Model-dependent retention defaults remain unchanged. See [OpenAI prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching).
- **Anthropic:** the official SDK sends one top-level `cache_control` with a five-minute ephemeral TTL, allowing the breakpoint to move with the conversation. Existing content/tool breakpoints or a caller-supplied raw request factory take precedence. Cache reads and writes remain distinct usage counters. See [Anthropic prompt caching](https://platform.claude.com/docs/en/build-with-claude/prompt-caching).
- **Google:** supported Gemini models provide implicit prefix caching. Stable tools, instructions and history improve reuse; this harness does not create paid cache-storage objects or inject another provider's fields. See [Gemini context caching](https://ai.google.dev/gemini-api/docs/caching).
- **ChatGPT subscription:** the supported Responses contract and service-managed behavior remain authoritative. No API-only cache controls are added; cache-read usage is displayed when returned.

`ShadowAgentProviderOptions.OptimizePromptCaching = false` omits harness-added hints; it does not disable a provider's automatic cache. A cache hit is never promised. Actual savings depend on model minimums, prefix reuse, cache lifetime and cache-write pricing; the UI shows observed tokens rather than estimated monetary savings. No credentials enter cache keys or portable checkpoints.

`SetPlan` accepts up to 64 `AgentPlanStep` values with `pending`, `in_progress` or `completed` status and at most one active step. The intrinsic `agent_plan` control uses the same validation in every host. `Pin` and `Unpin` manage up to 32 retained reference entries, each at most 32,000 characters.

## Context management and checkpoints

The context meter conservatively estimates UTF-8 input, including instructions, tool schemas, history, summary and pins. Actual provider input usage calibrates the estimate upward. It is not an exact model tokenizer. Configure `ContextWindowTokens` for the selected model and reserve suitable output space.

Before an inference crosses `CompactionThreshold`, the harness summarizes completed older rounds while retaining recent messages and keeping tool calls paired with their results. It archives the complete older transcript in `agent-history/` before replacing it with continuation notes. The current task and pinned context survive compaction. If the summarizer fails, bounded deterministic excerpts and the complete archive provide a fallback. If the workspace cannot retain the archive, compaction stops rather than discarding the original history.

Large tool outputs are stored in `agent-results/` with a preview and a path for `workspace_read` paging. `MaxToolResultCharacters` limits what is returned directly to model context; the registry and workspace have their own upstream size limits. Register workspace tools for the same archive workspace if the model should retrieve those files.

When idle:

```csharp
var pin = agent.Pin("Preserve drawing units and existing layer names.");
await agent.CompactAsync();
string checkpointJson = agent.Export();

// Create another AgentSession with fresh provider credentials and the same tool host.
restoredAgent.Import(checkpointJson);
await restoredAgent.RunAsync(""); // Continue the retained task.
```

Checkpoints are versioned JSON containing session identity, active task, summary, transcript, pins, workspace files, plan and run progress. They preserve portable text and tool call/results, provider reasoning signatures/protected data with their `reasoningProvider` origin, OpenAI reasoning item IDs and Anthropic redacted-thinking markers. Gemini's adjacent thought-signature content remains ordered correctly. The ChatGPT normalizer restores the app's `shadow` function-call namespace when the current .NET SDK re-encodes portable history.

The optional schema-1 `receipts` ledger preserves completed and failed/uncertain tool attempts through compaction and reload. Entries contain `callId`, `signature`, `isError` and optional `redactedSignature`. The authoritative signature is uppercase SHA-256 over compact JSON `[toolName, arguments]`, with recursively sorted object keys, preserved array order and exact number lexemes. An identical call ID/signature returns its prior receipt; changed arguments under that ID are rejected. The secondary redacted signature only validates retained arguments after known-secret redaction during import. Recent exact results remain available; compacted results are replaced on repeat calls by historical status requiring current-state inspection, never fresh verification. Reservations made before dispatch survive cancellation to prevent blind replay of uncertain attempts. The ledger permits 16,384 entries within a conservative 4 MiB budget and rejects new calls before dispatch at capacity; authoritative IDs are never evicted. Legacy schema-1 checkpoints without a ledger still import, rebuilding receipts only from retained completed call/result pairs.

Checkpoints exclude provider API keys, OAuth credentials, SDK clients, transport objects, permission options and arbitrary system instructions. The importing host supplies current credentials, instructions, tools and permission policy. Import validates roles, collection budgets, paths and complete call/result pairs before changing the session. Provider clients replay reasoning only when its recorded origin matches the selected provider; Google/Gemini aliases share an origin, while OpenAI API and ChatGPT subscription sessions remain distinct. Switching providers keeps visible conversation and tool call/result pairs but omits incompatible reasoning from inference. Untagged reasoning in legacy checkpoints is also omitted because its opaque signatures cannot be attributed safely. The stored checkpoint is preserved, so returning to its original provider can reuse tagged continuation data. A checkpoint does not serialize the live DXF document or roll back its edits; restore the document independently and inspect fresh revision/file receipts before further changes.

Transcript and workspace content may contain user data or secrets that a user/tool deliberately included. The optional session `redactor` is applied to events and exported text; hosts should redact their known session credentials and choose their own persistence policy. The package does not write checkpoints, API keys or OAuth tokens to disk automatically.

## Official SDK browser bridge

Applications with a connected local companion can create a portable streaming client directly:

```csharp
using var client = ShadowAgentProviders.CreateBrowserBridge(
    new ShadowAgentProviderOptions
    {
        Provider = "anthropic",
        Model = selectedModel,
        ApiKey = sessionApiKey,
        Endpoint = optionalProviderEndpoint,
    },
    endpoint: "http://127.0.0.1:5091",
    token: companionBridgeToken);
```

The bridge sends a typed `ShadowAgentBridgeRequest` to `/providers/stream`. The API key is passed only to the explicitly selected companion for that request; the companion constructs an official vendor client. An explicit endpoint override follows the same HTTPS/loopback validation as direct provider creation. All ordinary portable `ChatOptions` are serialized using Microsoft's chat contracts, with function declarations transported separately. Opaque SDK factories and background continuation are rejected because they cannot be transported as portable execution state.

Updates use a bounded NDJSON envelope carrying `ChatResponseUpdate`, with terminal completion/error frames. Microsoft's serializers preserve content, protected reasoning, tool calls/results and usage while excluding opaque `RawRepresentation`. The bridge forwards cancellation and transient failure metadata, and performs no inference or tool retries of its own. The browser harness remains responsible for tool execution, policy, retry budgets and context management.

## Sign in with ChatGPT

`netDxf.Shadow.Agents.Auth.ChatGptSignInClient` implements the official Sign in with ChatGPT flow for open-source local applications:

```csharp
using netDxf.Shadow.Agents.Auth;

using var signIn = new ChatGptSignInClient();
var pending = await signIn.BeginAsync(new ChatGptSignInOptions
{
    AppName = "Your application name",
    HostId = persistedStableHostId,
    CallbackUri = new Uri("http://127.0.0.1:5091/auth/callback"),
    ClientId = previouslyIssuedClientId, // null on initial registration
});

// Open pending.AuthorizationUri in the user's browser.
// Your active loopback listener supplies the complete callback URI:
ChatGptCredential credential = await signIn.CompleteAsync(pending, callbackUri);
credential = await signIn.RefreshAsync(credential);
bool remoteRevocationConfirmed = await signIn.RevokeAsync(credential);
```

The application must start its exact `127.0.0.1` HTTP callback listener before `BeginAsync`. The documented dynamic registration uses `dynamic_agent_client` only for the initial authorization; retain the issued client ID afterward. `RegisteredClientId` on the attempt retains the validated nonsecret registration if a one-use code exchange fails and sign-in must restart. No client secret or borrowed third-party client ID is used.

The implementation validates PKCE, one-use state, trusted discovery/JWKS endpoints, ID-token signature, issuer, audience, expiry, nonce and expected account subject. It requires the resource-invocation and ChatGPT direct-plan scopes, coalesces rotating refreshes and supports revocation. Credentials are in-memory objects with redacted diagnostic strings and JSON-ignored token properties. Owners control their lifetime; the sample persists only nonsecret host/client/account mapping and clears OAuth credentials on restart.

Subscription inference uses the official public Responses endpoint, streaming, `store:false`, full input history and developer roles. `ChatGptResponsesContract.Normalize` enforces supported request fields and grouped local function tools. Hosted tool availability, plan usage consent, supported models and rate limits are set by OpenAI. A GitHub Pages app needs the local callback companion for this public-client flow; the page's HTTPS URL is not a supported replacement for that loopback callback.

Official documentation: [open-source integration](https://developers.openai.com/siwc/token-sharing-open-source), [sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [models and inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference), [current limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations).

## Budgets and packaging

Defaults are Normal mode, no fixed model-turn cutoff (`MaxTurns = 0`), an eight-turn no-progress budget, 8,192 output tokens per response, a 128,000-token context estimate, a 72% compaction threshold, four inference retries, four parallel reads, a three-minute inference timeout, a two-minute tool timeout and ten-minute approval expiry. These are host configuration through `AgentOptions`, not claims about provider entitlements. Positive model-turn budgets remain available and pause with recoverable state. `FullAccess` changes tool permission behavior while retaining the selected execution mode and resource budgets.

Build and pack from the repository root:

```sh
dotnet build shadow/src/netDxf.Shadow.Agents -c Release
dotnet pack shadow/src/netDxf.Shadow.Agents -c Release -o artifacts/packages
```

Consume the package with the same-version Shadow dependencies, or use its project reference in a local build. Packing creates a NuGet artifact; it does not publish the package to a feed. The browser Studio and ASP.NET companion are separate consumers.

Verification projects exercise the harness over real DXF edits with scripted models, the official SDKs over controlled provider streams, and the loopback companion with an official MCP client, WebSockets and cancellable native processes. Live provider credentials and account authorization remain user-supplied.
