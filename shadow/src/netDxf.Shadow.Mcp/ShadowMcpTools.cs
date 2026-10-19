using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Mcp;

/// <summary>Installs discoverable access to all captured objects, values, source metadata and editor transactions.</summary>
public sealed partial class ShadowMcpTools
{
    private sealed class SnapshotIndex(ShadowSnapshot snapshot)
    {
        public ShadowObject[] Ordered { get; } = snapshot.Objects.Values.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToArray();
        public Lazy<ImmutableDictionary<ObjectId, ImmutableHashSet<ObjectId>>> Incoming { get; } = new(snapshot.BuildDependents);
    }
    private static readonly ConditionalWeakTable<ShadowSnapshot, SnapshotIndex> Indices = new();
    private readonly ShadowMcpContext context;
    private ShadowMcpTools(ShadowMcpContext context) => this.context = context;

    public static McpToolRegistry Create(ShadowMcpContext context)
    {
        var registry = new McpToolRegistry();
        Register(registry, context);
        return registry;
    }

    public static void Register(McpToolRegistry registry, ShadowMcpContext context)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(context);
        new ShadowMcpTools(context).Install(registry);
    }

    private void Install(McpToolRegistry registry)
    {
        Add(registry, "shadow_state", "Read current document/workspace identity, revision, model counts, layouts and editor history. Call this before editing; copy revision, documentId, workspaceId and viewVersion into mutation arguments.", true, [], [],
            (a, e, s, ct) => new
            {
                root = s.Root.Value, objects = s.Objects.Count, entities = s.EntityIds.Count,
                sourceRevision = context.Source?.Invoke()?.Current.Revision,
                layouts = s.Layouts.Select(l => new { id = l.Id?.Value, name = l["Name"].Text }).ToArray(),
                history = e.State, valueFormat = "Atoms retain invariant text and CLR type. References use {$ref:id}; typed structures use {kind:structure,type,fields}. Paths are JSON Pointers, following references and indexing collections.",
                mutationIdentityRequired = context.RequireWorkspaceIdentity
            });
        Add(registry, "shadow_objects", "Page through every source-derived shadow node, including entities, blocks, tables, dictionaries, collections, vertices, header and resource objects. Filter by full/short type, name/text or kind. Read individual object paths with shadow_read.", true,
            Fields(("type", String()), ("search", String(512)), ("kind", String()), ("offset", Integer(0)), ("limit", Integer(1, 200))), [], Objects);
        Add(registry, "shadow_read", "Inspect any captured node or nested JSON Pointer path. References resolve between segments; numeric segments index sequences/collection $items. Pages expose nextOffset/nextTextOffset so large collections and text remain fully accessible.", true,
            Fields(("id", String()), ("path", String(8192)), ("depth", Integer(0, 6)), ("offset", Integer(0)), ("limit", Integer(1, 100)),
                ("textOffset", Integer(0)), ("textLimit", Integer(1, 16384))), [], Read);
        Add(registry, "shadow_schema", "Discover all properties and writable paths of a captured object, or constructor/property/enum metadata of any linked netDxf public data-model type, including types absent from the drawing.", true,
            Fields(("id", String()), ("type", String()), ("offset", Integer(0)), ("limit", Integer(1, 200))), [], Schema);
        Add(registry, "dxf_types", "Discover the linked netDxf public type catalog, including entity subclasses, nested geometry, objects, tables, headers, collections and enums. Use full names with shadow_schema and shadow_graph_edit.", true,
            Fields(("search", String(512)), ("entitiesOnly", Boolean()), ("offset", Integer(0)), ("limit", Integer(1, 200))), [], Types);
        Add(registry, "shadow_references", "Inspect incoming or outgoing reference edges of any shadow node. Reference paths identify exact nested structure/collection members; supports full pagination.", true,
            Fields(("id", String()), ("direction", Enum("outgoing", "incoming")), ("offset", Integer(0)), ("limit", Integer(1, 200))), ["id"], References);
        Add(registry, "shadow_traverse", "Traverse dependency/ownership/reference graphs from arbitrary roots with cycle detection, direction, depth and node limits. Reports whether traversal was truncated.", true,
            Fields(("ids", Array(String(), 1, 200)), ("direction", Enum("outgoing", "incoming")), ("depth", Integer(0, 16)), ("limit", Integer(1, 1000))), ["ids"], Traverse);
        Add(registry, "shadow_diagnostics", "Page through current capture/model diagnostics, optionally filtering by object or severity.", true,
            Fields(("id", String()), ("severity", Enum("Information", "Warning", "Error")), ("offset", Integer(0)), ("limit", Integer(1, 200))), [], Diagnostics);
        Add(registry, "shadow_history", "Read immutable undo descriptions and undo/redo counts for the active workspace.", true,
            Fields(("offset", Integer(0)), ("limit", Integer(1, 200))), [], (a, e, s, ct) =>
                Page(e.UndoDescriptions, GetInt(a, "offset", 0), GetInt(a, "limit", 100), d => d));
        Add(registry, "dxf_source_read", "Read the canonical live source shadow graph, even while a branch is active. All identities/values remain detached; sourceRevision identifies the captured live document. Use dxf_source_inspect for current public source properties.", true,
            Fields(("id", String()), ("path", String(8192)), ("depth", Integer(0, 6)), ("offset", Integer(0)), ("limit", Integer(1, 100)),
                ("textOffset", Integer(0)), ("textLimit", Integer(1, 16384))), [], (a, e, s, ct) =>
            {
                var source = context.Source?.Invoke() ?? throw new NotSupportedException("This host has no mutable netDxf source adapter.");
                return new { sourceRevision = source.Current.Revision, sourceDocumentId = source.Current.DocumentId, value = Read(a, e, source.Current, ct) };
            });
        Add(registry, "dxf_source_inspect", "Inspect current public netDxf source properties under the source read gate. Start at the document or a DXF handle; follow a public-property/collection-index JSON Pointer. Property/collection pages use offset/limit; strings use textOffset/textLimit. Returns bounded detached values and never exposes live CLR references.", true,
            Fields(("handle", String(128)), ("path", String(8192)), ("depth", Integer(0, 4)), ("offset", Integer(0, 1000000)), ("limit", Integer(1, 100)),
                ("textOffset", Integer(0)), ("textLimit", Integer(1, 16384))), [], SourceInspect);
        InstallEditing(registry);
        InstallDrawing(registry);
        InstallSnapshots(registry);
        InstallGraph(registry);
    }

    private object Objects(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var type = GetString(args, "type"); var search = GetString(args, "search"); var kind = GetString(args, "kind");
        ShadowObjectKind? filterKind = null;
        if (kind is not null)
        {
            if (!System.Enum.TryParse<ShadowObjectKind>(kind, true, out var parsed) || !System.Enum.IsDefined(parsed)) throw new ArgumentException("Unknown shadow object kind.");
            filterKind = parsed;
        }
        var offset = GetInt(args, "offset", 0); var limit = GetInt(args, "limit", 50);
        var result = new List<object>(limit); var count = 0; var scanned = 0;
        foreach (var node in Indices.GetValue(snapshot, s => new(s)).Ordered)
        {
            if ((scanned++ & 127) == 0) token.ThrowIfCancellationRequested();
            if (type is not null && !string.Equals(type, node.TypeName, StringComparison.OrdinalIgnoreCase) && !string.Equals(type, node.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (filterKind is { } selected && (selected == ShadowObjectKind.Object ? node.Kind != selected : (node.Kind & selected) == 0)) continue;
            if (search is not null && !node.Id.Value.Contains(search, StringComparison.OrdinalIgnoreCase) && !node.TypeName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !node.Properties.Values.OfType<ShadowValue.Atom>().Any(a => a.Text.Contains(search, StringComparison.OrdinalIgnoreCase))) continue;
            if (count++ < offset || result.Count >= limit) continue;
            result.Add(new { id = node.Id.Value, type = node.TypeName, kind = node.Kind.ToString(), name = (node["Name"] as ShadowValue.Atom)?.Text, properties = node.Properties.Count });
        }
        return new { total = count, offset, items = result, nextOffset = offset + result.Count < count ? (int?)(offset + result.Count) : null };
    }

    private object Read(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = new ObjectId(GetString(args, "id") ?? snapshot.Root.Value);
        var node = Find(snapshot, id); var path = GetString(args, "path") ?? "";
        var depth = GetInt(args, "depth", 3); var limit = GetInt(args, "limit", 32); var offset = GetInt(args, "offset", 0);
        var textOffset = GetInt(args, "textOffset", 0); var textLimit = GetInt(args, "textLimit", 4096);
        if (path.Length > 0)
        {
            var location = ShadowJson.Locate(snapshot, id, path);
            var value = location.Value;
            // Collection references can be paged directly without an extra $items lookup.
            if (value is ShadowValue.Reference reference && snapshot.Objects[reference.Id]["$items"] is ShadowValue.Sequence members) value = members;
            return new { id = id.Value, path, value = ShadowJson.Describe(value, depth, limit, offset, textOffset, textLimit), resolvedObject = location.Object.Value, property = location.Property };
        }
        return new
        {
            id = id.Value, type = node.TypeName, kind = node.Kind.ToString(),
            properties = Page(node.Properties.OrderBy(p => p.Key, StringComparer.Ordinal), offset, limit, p => new
            {
                name = p.Key, path = "/" + ShadowJson.Escape(p.Key), writable = node.WritableProperties.Contains(p.Key),
                value = ShadowJson.Describe(p.Value, depth, Math.Min(limit, 16), textOffset: textOffset, textLimit: textLimit)
            })
        };
    }

    private object Schema(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GetString(args, "type") is { } name) return DxfTypeCatalog.Describe(DxfTypeCatalog.Resolve(name), GetInt(args, "offset", 0), GetInt(args, "limit", 100));
        var id = new ObjectId(GetString(args, "id") ?? snapshot.Root.Value); var node = Find(snapshot, id);
        return new
        {
            id = id.Value, type = node.TypeName, kind = node.Kind.ToString(),
            properties = Page(node.Properties.OrderBy(p => p.Key, StringComparer.Ordinal), GetInt(args, "offset", 0), GetInt(args, "limit", 100), p => new
            {
                name = p.Key, path = "/" + ShadowJson.Escape(p.Key), writable = node.WritableProperties.Contains(p.Key),
                kind = p.Value.GetType().Name,
                type = p.Value is ShadowValue.Atom a ? a.TypeName : p.Value is ShadowValue.Structure v ? v.TypeName : null,
                reference = (p.Value as ShadowValue.Reference)?.Id.Value,
                count = p.Value is ShadowValue.Sequence sequence ? (int?)sequence.Items.Length : null
            })
        };
    }

    private object Types(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var search = GetString(args, "search"); var entitiesOnly = GetBool(args, "entitiesOnly");
        return Page(DxfTypeCatalog.Types.Where(t => (search is null || t.FullName!.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (!entitiesOnly || typeof(netDxf.Entities.EntityObject).IsAssignableFrom(t))), GetInt(args, "offset", 0), GetInt(args, "limit", 100), t => new
            { name = t.FullName, t.IsAbstract, t.IsEnum, valueType = t.IsValueType, kind = DxfTypeCatalog.Kind(t).ToString() });
    }

    private object Diagnostics(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = GetString(args, "id"); var severity = GetString(args, "severity");
        return Page(snapshot.Diagnostics.Where(d => (id is null || d.Object?.Value == id) && (severity is null || d.Severity.ToString() == severity)),
            GetInt(args, "offset", 0), GetInt(args, "limit", 100), d => new { d.Code, d.Message, id = d.Object?.Value, severity = d.Severity.ToString() });
    }

    private object References(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var id = new ObjectId(RequiredString(args, "id")); Find(snapshot, id);
        IEnumerable<(ObjectId Source, string Path, ObjectId Target)> edges;
        if (GetString(args, "direction") == "incoming")
        {
            var incoming = Indices.GetValue(snapshot, s => new(s)).Incoming.Value.GetValueOrDefault(id) ?? [];
            edges = incoming.OrderBy(i => i.Value, StringComparer.Ordinal).SelectMany(source => Edges(snapshot.Objects[source])).Where(e => e.Target == id);
        }
        else edges = Edges(snapshot.Objects[id]);
        token.ThrowIfCancellationRequested();
        return Page(edges, GetInt(args, "offset", 0), GetInt(args, "limit", 100), e => new { source = e.Source.Value, path = e.Path, target = e.Target.Value });
    }

    private object Traverse(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var maxDepth = GetInt(args, "depth", 2); var limit = GetInt(args, "limit", 200); var reverse = GetString(args, "direction") == "incoming";
        var incoming = reverse ? Indices.GetValue(snapshot, s => new(s)).Incoming.Value : null;
        var queue = new Queue<(ObjectId Id, int Depth)>(); var visited = new HashSet<ObjectId>(); var nodes = new List<object>();
        foreach (var item in args.GetProperty("ids").EnumerateArray()) { var id = new ObjectId(item.GetString()!); Find(snapshot, id); queue.Enqueue((id, 0)); }
        while (queue.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(current.Id)) continue;
            var node = snapshot.Objects[current.Id];
            nodes.Add(new { id = node.Id.Value, type = node.TypeName, depth = current.Depth });
            if (nodes.Count >= limit) return new { nodes, truncated = queue.Count > 0 || current.Depth < maxDepth, maxDepth, limit };
            if (current.Depth >= maxDepth) continue;
            IEnumerable<ObjectId> neighbors = reverse ? incoming!.GetValueOrDefault(current.Id) ?? [] : node.Properties.Values.SelectMany(v => v.References()).Distinct();
            foreach (var neighbor in neighbors.OrderBy(i => i.Value, StringComparer.Ordinal))
                if (!visited.Contains(neighbor)) queue.Enqueue((neighbor, current.Depth + 1));
        }
        return new { nodes, truncated = false, maxDepth, limit };
    }

    private static IEnumerable<(ObjectId Source, string Path, ObjectId Target)> Edges(ShadowObject node)
    {
        var pending = new Stack<(string Path, ShadowValue Value)>();
        foreach (var property in node.Properties.OrderByDescending(p => p.Key, StringComparer.Ordinal)) pending.Push(("/" + ShadowJson.Escape(property.Key), property.Value));
        while (pending.TryPop(out var item))
        {
            switch (item.Value)
            {
                case ShadowValue.Reference reference: yield return (node.Id, item.Path, reference.Id); break;
                case ShadowValue.Sequence sequence:
                    for (var i = sequence.Items.Length - 1; i >= 0; i--) pending.Push((item.Path + "/" + i.ToString(CultureInfo.InvariantCulture), sequence.Items[i]));
                    break;
                case ShadowValue.Structure structure:
                    foreach (var field in structure.Fields.OrderByDescending(p => p.Key, StringComparer.Ordinal)) pending.Push((item.Path + "/" + ShadowJson.Escape(field.Key), field.Value));
                    break;
            }
        }
    }

    private void Add(McpToolRegistry registry, string name, string description, bool readOnly, Dictionary<string, object> fields, string[] required,
        Func<JsonElement, EditorSession, ShadowSnapshot, CancellationToken, object?> operation)
    {
        fields.TryAdd("documentId", String()); fields.TryAdd("workspaceId", String());
        fields.TryAdd("viewVersion", new { type = "integer", minimum = 0 }); fields.TryAdd("revision", new { type = "integer", minimum = 0 });
        if (!readOnly) required = [..required, "revision", ..context.RequireWorkspaceIdentity ? new[] { "documentId", "workspaceId", "viewVersion" } : System.Array.Empty<string>()];
        var schema = McpJson.Element(new { type = "object", properties = fields, required, additionalProperties = false });
        registry.Register(new(name, description, schema, readOnly), (args, token) => new(context.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var editor = context.Editor(); var before = editor.Snapshot; var identity = context.GetIdentity(before);
            CheckIdentity(args, before, identity);
            var data = operation(args, editor, before, token);
            var after = context.Editor().Snapshot;
            if (!readOnly && !GetBool(args, "preview"))
                try { context.Changed?.Invoke(); }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
                { /* Host invalidation is observational; a committed edit must remain a successful receipt. */ }
            var currentIdentity = context.GetIdentity(after);
            return McpToolResult.FromObject(CreateReceipt(after, currentIdentity, data));
        })));
    }

    private static object CreateReceipt(ShadowSnapshot snapshot, ShadowMcpIdentity identity, object? data) => new
    {
        documentId = identity.DocumentId, workspaceId = identity.WorkspaceId,
        viewVersion = identity.ViewVersion, revision = snapshot.Revision, data
    };

    private static void CheckIdentity(JsonElement args, ShadowSnapshot snapshot, ShadowMcpIdentity identity)
    {
        if (args.TryGetProperty("revision", out var revision) && (!revision.TryGetInt64(out var expected) || expected != snapshot.Revision))
            throw new ShadowConflictException("The model revision changed.");
        if (GetString(args, "documentId") is { } document && (!Guid.TryParse(document, out var parsed) || parsed != identity.DocumentId) ||
            GetString(args, "workspaceId") is { } workspace && workspace != identity.WorkspaceId ||
            args.TryGetProperty("viewVersion", out var view) && (!view.TryGetInt64(out var expectedView) || expectedView != identity.ViewVersion))
            throw new ShadowConflictException("The document or workspace changed.");
    }

    private static ShadowObject Find(ShadowSnapshot snapshot, ObjectId id) => snapshot.Objects.TryGetValue(id, out var node) ? node : throw new ArgumentException($"Unknown object '{id}'.");
    private static string? GetString(JsonElement args, string name) => args.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static string RequiredString(JsonElement args, string name) => GetString(args, name) is { Length: > 0 } value ? value : throw new ArgumentException($"'{name}' is required and must be nonempty.");
    private static int GetInt(JsonElement args, string name, int fallback) => args.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
    private static bool GetBool(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.GetBoolean();
    private static object Page<T, TResult>(IEnumerable<T> items, int offset, int limit, Func<T, TResult> map)
    {
        var page = new List<TResult>(limit); var total = 0;
        foreach (var item in items) { if (total++ >= offset && page.Count < limit) page.Add(map(item)); }
        return new { total, offset, items = page, nextOffset = offset + page.Count < total ? (int?)(offset + page.Count) : null };
    }
    private static object String(int maximum = 2048) => new { type = "string", maxLength = maximum };
    private static object Integer(int minimum, int maximum = int.MaxValue) => new { type = "integer", minimum, maximum };
    private static object Boolean() => new { type = "boolean" };
    private static object Enum(params string[] values) => new { type = "string", @enum = values };
    private static object Array(object items, int minimum = 0, int maximum = 256) => new { type = "array", items, minItems = minimum, maxItems = maximum };
    private static Dictionary<string, object> Fields(params (string Name, object Schema)[] fields) => fields.ToDictionary(f => f.Name, f => f.Schema, StringComparer.Ordinal);
}
