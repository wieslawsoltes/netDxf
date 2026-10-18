# netDxf.Shadow.Mcp

Transport-independent MCP tools for the complete captured netDxf graph, native source inspection, immutable snapshots, reference analysis and revision-checked editing. The package depends on `netDxf.Shadow.NetDxf`, `netDxf.Shadow.Editor` and the official `ModelContextProtocol.Core` 2.2.0 SDK. It has no ASP.NET Core, desktop, browser UI or provider-credential dependency.

## Attach a document

```csharp
using netDxf;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;

var source = new ShadowSession(new DxfDocument());
var editor = new EditorSession(source);
var tools = ShadowMcpTools.Create(new(() => editor, () => source));

var state = await tools.ExecuteAsync("shadow_state", McpJson.EmptyObject);
Console.WriteLine(state.Text);
```

`IMcpToolExecutor` is also consumed directly by `netDxf.Shadow.Agents`. Applications extend `McpToolRegistry` with their own commands. Registration clones JSON schemas; argument validation, cancellation and bounded model-visible error results are shared by direct agent execution and MCP transports.

```csharp
tools.Register(
    new McpToolDefinition("application_status", "Read application status.",
        McpJson.Parse("""{"type":"object","additionalProperties":false}"""), ReadOnly: true),
    (arguments, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(McpToolResult.FromObject(new { ready = true }));
    });
```

## Catalog

| Tools | Access |
| --- | --- |
| `shadow_state` | Document/workspace identity, revision, layout roots, selection and undo/redo counts. |
| `shadow_objects` | Search and page all entities, tables, collections, vertices, dictionaries, header objects and resources. |
| `shadow_read` | Any node, JSON Pointer path, referenced object, structure field, collection item or text range. |
| `shadow_schema`, `dxf_types` | Captured writable paths and linked public type/constructor/property/field/enum metadata, including types absent from the drawing. |
| `shadow_references`, `shadow_traverse` | Incoming/outgoing paths and bounded graph traversal with cycle detection. |
| `shadow_diagnostics`, `shadow_history` | Paged diagnostics and undo descriptions. |
| `dxf_source_read`, `dxf_source_inspect` | Detached canonical source graph and serialized public source inspection, including when a branch is active. |
| `shadow_edit` | Atomic property transactions, including several changes to the same nested structure. |
| `shadow_graph_edit` | Atomic node construction, entity attachment, structural values, collection replacement, deletion and pruning. |
| `shadow_undo`, `shadow_redo`, `shadow_select` | Existing editor history and selection. |
| `shadow_checkpoint`, `shadow_checkpoints`, `shadow_diff` | O(1) immutable snapshot retention and paged changes across revisions or branches of one document. |

Read results carry top-level `documentId`, `workspaceId`, `viewVersion`, `revision` and `data`. Mutations always require `revision`. A multi-workspace host sets `RequireWorkspaceIdentity = true` and supplies all four fields. This rejects stale requests even when two documents or branches happen to have the same numeric revision.

The shared Studio integration resolves the current editor/source on every invocation under the existing Studio gate. Loading a document, switching a branch or closing a document cannot leave a registry targeting a stale editor. Standalone contexts serialize their own registry operations; hosts that edit the same document through other APIs should provide `Synchronize` using their existing single-writer gate.

## Values and edits

Scalar values retain both their CLR type and invariant text, avoiding loss of `UInt64`, decimal or round-trip floating-point precision. References use object IDs, never mutable CLR instances. A path such as `/StartPoint/X` edits a coordinate; `/Vertexes/2/Bulge` follows a collection reference and indexes its members. JSON Pointer escapes are `~0` for `~` and `~1` for `/`.

```json
{
  "revision": 12,
  "description": "Move the line start",
  "edits": [
    {"id": "H:42", "path": "/StartPoint/X", "value": 25},
    {"id": "H:42", "path": "/StartPoint/Y", "value": 40}
  ]
}
```

Use IDs and identity receipts obtained from the current drawing; the values above illustrate the shape. Typed input supports:

```json
{"kind":"atom","type":"System.UInt64","value":"18446744073709551615"}
{"$ref":"H:42"}
{"kind":"structure","type":"netDxf.Vector3","fields":{"X":1,"Y":2,"Z":0}}
{"kind":"sequence","items":[{"$ref":"H:42"},{"$ref":"H:43"}]}
```

`shadow_graph_edit` accepts ordered `operations`. `create` and `addEntity` accept full type names and infer declared member types from creation properties. New objects can receive an `@alias`, usable by subsequent operations and references in the same atomic transaction:

```json
{
  "revision": 12,
  "description": "Create a line and layer",
  "operations": [
    {"op":"create","type":"netDxf.Tables.Layer","alias":"@layer","properties":{"Name":"Agent layer"}},
    {"op":"addEntity","type":"netDxf.Entities.Line","alias":"@line","properties":{
      "StartPoint":{"X":0,"Y":0,"Z":0},
      "EndPoint":{"X":100,"Y":50,"Z":0},
      "Layer":{"$ref":"@layer"}
    }}
  ]
}
```

Other structural operations are `set` (`id`, `path`, `value`), `replaceCollection` (`id`, `path`, `items`), `removeEntities` (`ids`), `removeObject` (`id`) and `pruneUnreachable`. Ordered operations must maintain valid references. A source object must ultimately be attached to the public document graph. Constructor requirements, native ownership rules and unsupported semantic changes are reported through the existing source adapter; callers may register audited `ShadowObjectFactories` for custom construction.

Property and structural writes pass through `EditorSession` and `ShadowSession`; native preparation, validation, canonicalization, rollback and undo remain authoritative. `preview: true` validates the immutable proposal without publishing a source change. Its result explicitly states that native setter/constructor validation runs when applying the transaction.

Reads are paged and bounded. `nextOffset` and `nextTextOffset` identify continuation ranges; nested previews collapse before exceeding their node/text budgets. Partial typed previews are rejected as replacement values, preventing an accidentally replayed preview from deleting unseen collection members or truncating text. The registry limits input to 1 MiB of characters and output to 128 KiB by default. A successful oversized operation stays successful and returns a bounded truncation receipt; clients should query narrower paths for complete data. Checkpoints retain at most eight immutable snapshots per registry.

Type schemas accept `offset`/`limit` and report `totalProperties` and `nextOffset`. The linked public catalog includes unit enums, analytic geometry types and raw IO metadata; `sourceConstructible` distinguishes the native graph factory's supported data-model scope. Direct `dxf_source_inspect` string values expose `value`, `length`, `textOffset` and `nextTextOffset`; request a narrow property path with `textOffset`/`textLimit` to retrieve any part of a large source string.

## Official MCP transports

Stream hosts can use the SDK server directly:

```csharp
await using var server = ShadowMcpServer.Create(
    Console.OpenStandardInput(), Console.OpenStandardOutput(), tools);
await server.RunAsync(cancellationToken);
```

ASP.NET hosts add `ModelContextProtocol.AspNetCore` separately, then use `ShadowMcpServer.Configure(options, executor)` with `AddMcpServer` / `WithHttpTransport` / `MapMcp`. `ShadowMcpServer.CreateOptions(executor)` is also available. The executor can proxy to an attached browser, so the same public catalog works for an external coding agent without duplicating source editing logic.

Browsers and other in-process clients can send ordinary JSON-RPC messages through the SDK-backed bridge:

```csharp
await using var protocol = new McpJsonRpcSession(tools);
var initialized = await protocol.ProcessAsync("""
    {"jsonrpc":"2.0","id":1,"method":"initialize","params":{
      "protocolVersion":"2025-11-25","capabilities":{},
      "clientInfo":{"name":"my-browser","version":"1.0"}
    }}
    """);
await protocol.ProcessAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
var catalog = await protocol.ProcessAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
```

The official SDK owns protocol negotiation, lifecycle and handlers. Its pinned version supports revisions through `2026-07-28` as well as earlier initialization handshakes. The bridge uses bounded in-memory pipes, unique request-ID correlation, at most 64 outstanding calls and cooperative cancellation notifications. It supports standard tools, resources and prompts:

- `shadow://server/tools` and `shadow://server/instructions`
- `shadow://workspace/state` and `shadow://workspace/diagnostics`
- `shadow://workspace/objects/{id}` with optional `path`, `offset`, `limit`, `depth`, `textOffset`, `textLimit` query values
- `shadow://schema/types/{type}` with optional `offset` and `limit`
- `inspect_drawing` and `edit_drawing` prompts

The browser Studio runs this official protocol session over granted `MessagePort` connections and registers the same tools through WebMCP when `document.modelContext` is available. Browser agents and same-page integrations can control the live document without a server or companion. See the [browser MCP guide](../../docs/browser-mcp.md) for connection examples, permissions, request receipts and cancellation. Native clients can use the optional Studio companion's authenticated HTTP MCP endpoint.

## Verification and protocol references

```bash
dotnet run --project shadow/tests/netDxf.Shadow.Mcp.Verification
```

The executable suite verifies native source writes and rollback, structural creation/ownership/undo, branch isolation, metadata, pagination, checkpoint diffs and official MCP wire messages, including concurrent calls and cancellation.

- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [Pinned SDK server options](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Server/McpServerOptions.cs)
- [Official SDK transports](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/transports/transports.html)
