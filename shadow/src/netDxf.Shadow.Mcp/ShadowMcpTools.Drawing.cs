using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Entities;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private const int MaximumCreatedObjects = 4096;

    private void InstallDrawing(McpToolRegistry registry)
    {
        var properties = new { type = "object", maxProperties = 256 };
        var entity = new
        {
            type = "object", properties = Fields(("type", String()), ("alias", String(128)), ("properties", properties), ("layout", String()), ("owner", String())),
            additionalProperties = false
        };
        var resource = new
        {
            type = "object", properties = Fields(("type", String()), ("alias", String(128)), ("properties", properties)),
            required = new[] { "type" }, additionalProperties = false
        };
        Add(registry, "shadow_create_entities", "Rapidly create 1–1024 entities in ONE revision-checked undoable transaction. Top-level type/properties/layout/owner supply defaults; item properties override matching defaults. Optional resources create layers, blocks and other objects before entities. Reference earlier aliases with {$ref:'@name'}. Nested property objects and arrays construct declared netDxf types (e.g. Polyline2D.Vertexes); $type selects an assignable public subtype. At most 4096 total new objects. Every created object must attach to the document. preview validates the graph only; apply uses native constructors/ownership/rollback.", false,
            Fields(("description", String(512)), ("type", String()), ("properties", properties), ("layout", String()), ("owner", String()),
                ("resources", Array(resource, 0, 256)), ("entities", Array(entity, 1, 1024)), ("preview", Boolean())), ["entities"], CreateEntities);
        var splice = new
        {
            type = "object", properties = Fields(("id", String()), ("path", String(8192)), ("index", Integer(0)), ("deleteCount", Integer(0, 10000)),
                ("items", Array(new { description = "Typed values, existing $ref objects, or inline declared netDxf objects. Omit to delete only." }, 0, 10000))),
            required = new[] { "id", "path", "index" }, additionalProperties = false
        };
        Add(registry, "shadow_collection_edit", "Insert, append, remove or replace slices of captured collections/embedded sequences in ONE atomic editor transaction. Each edit uses id/path/index/deleteCount/items; index may equal collection length to append. Edits compose in request order. Inline inserted objects use the declared collection element type; use $type for abstract elements. Removed unreachable owned objects are pruned. Existing references, ownership rules and fixed array sizes remain enforced by the native adapter. preview validates the graph only.", false,
            Fields(("description", String(512)), ("edits", Array(splice, 1, 256)), ("preview", Boolean())), ["edits"], EditCollections);
        Add(registry, "dxf_methods", "Discover every public non-accessor method of a linked netDxf type, including inherited/static overloads and exact signatures. Metadata marks methods callable through dxf_method_call; unsupported methods remain discoverable with an explicit reason. No reflection execution or external code loading is exposed.", true,
            Fields(("type", String()), ("id", String()), ("search", String(512)), ("callableOnly", Boolean()), ("offset", Integer(0)), ("limit", Integer(1, 200))), [], Methods);
        var call = new
        {
            type = "object", properties = Fields(("id", String()), ("method", String()), ("arguments", new
            {
                type = "object", properties = Fields(("axis1", new { type = "number", minimum = double.Epsilon }), ("axis2", new { type = "number", minimum = double.Epsilon })),
                required = new[] { "axis1", "axis2" }, additionalProperties = false
            })), required = new[] { "id", "method", "arguments" }, additionalProperties = false
        };
        Add(registry, "dxf_method_call", "Execute up to 256 audited public-method adapters as ONE undoable editor/source transaction. Discover support with dxf_methods. Currently Ellipse.SetAxis(axis1,axis2) is supported; axes are normalized exactly like netDxf. Other methods are rejected before publication. preview validates the immutable proposal; apply retains native validation/rollback. Arbitrary methods, reflection/code evaluation and file/network calls are unavailable.", false,
            Fields(("description", String(512)), ("calls", Array(call, 1, 256)), ("preview", Boolean())), ["calls"], CallMethods);
    }

    private object CreateEntities(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var graph = new ShadowGraphEdit(snapshot);
        var aliases = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var construction = new Construction(graph, Resolve, token);
        var created = new List<object>();
        if (args.TryGetProperty("resources", out var resources))
            foreach (var resource in resources.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                var type = DxfTypeCatalog.Resolve(RequiredString(resource, "type"));
                var id = construction.Create(type, resource.TryGetProperty("properties", out var values) ? values : McpJson.EmptyObject);
                AddAlias(resource, id);
            }
        var defaults = args.TryGetProperty("properties", out var defaultValues) ? defaultValues : McpJson.EmptyObject;
        var index = 0;
        foreach (var entity in args.GetProperty("entities").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var type = DxfTypeCatalog.Resolve(GetString(entity, "type") ?? RequiredString(args, "type"));
            if (!typeof(EntityObject).IsAssignableFrom(type) || !DxfTypeCatalog.IsSourceConstructible(type))
                throw new ArgumentException($"{type.FullName} is not a constructible entity type.");
            var merged = defaults.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            if (entity.TryGetProperty("properties", out var supplied)) foreach (var p in supplied.EnumerateObject()) merged[p.Name] = p.Value;
            var ownerName = GetString(entity, "owner") ?? GetString(args, "owner");
            var owner = ownerName is not null ? Resolve(ownerName) :
                snapshot.Layout(GetString(entity, "layout") ?? GetString(args, "layout") ?? "Model")["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown layout.");
            var values = construction.Properties(type, McpJson.Element(merged));
            construction.CountObject();
            var id = graph.AddEntity(owner, type.FullName!, values);
            AddAlias(entity, id);
            created.Add(new { index = index++, id = id.Value, type = type.FullName, alias = GetString(entity, "alias") });
        }
        var patch = graph.Build();
        token.ThrowIfCancellationRequested();
        ValidateCreatedAttachments(snapshot, patch, token);
        var preview = GetBool(args, "preview");
        var result = preview ? patch.Apply(snapshot) : editor.Execute(GetString(args, "description") ?? "Create drawing entities", patch);
        return new
        {
            preview, atomic = true, validation = Validation(preview), resultingRevision = result.Revision,
            count = created.Count, createdObjects = construction.Count, entities = created, aliases = aliases.ToDictionary(p => p.Key, p => p.Value.Value),
            history = editor.State
        };

        ObjectId Resolve(string id) => id.StartsWith('@') ? aliases.TryGetValue(id, out var resolved) ? resolved : throw new ArgumentException($"Unknown transaction alias '{id}'.") : new(id);
        void AddAlias(JsonElement input, ObjectId id)
        {
            if (GetString(input, "alias") is not { } alias) return;
            if (!alias.StartsWith('@') || alias.Length < 2 || !aliases.TryAdd(alias, id)) throw new ArgumentException("Each creation alias must be unique and start with '@'.");
        }
    }

    private object EditCollections(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var working = snapshot;
        var edits = new List<object>();
        var createdCount = 0;
        foreach (var edit in args.GetProperty("edits").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var location = ShadowJson.Locate(working, new(RequiredString(edit, "id")), RequiredString(edit, "path"));
            var selected = location.Value;
            if (selected is ShadowValue.Reference reference) selected = Find(working, reference.Id)["$items"];
            if (selected is not ShadowValue.Sequence sequence) throw new ArgumentException("The selected path is not a collection or sequence.");
            var index = GetInt(edit, "index", 0); var delete = GetInt(edit, "deleteCount", 0);
            if (index > sequence.Items.Length || delete > sequence.Items.Length - index) throw new ArgumentException("Collection slice is out of range; index and deleteCount must fit the current sequence.");
            var graph = new ShadowGraphEdit(working);
            var construction = new Construction(graph, id => new(id), token, createdCount);
            var elementType = CollectionElementType(working, location);
            var inserted = edit.TryGetProperty("items", out var items)
                ? items.EnumerateArray().Select(value => elementType is null ? ShadowJson.FromInput(value) : DxfTypeCatalog.ReadTyped(value, elementType, id => new(id), construction.Inline)).ToImmutableArray()
                : ImmutableArray<ShadowValue>.Empty;
            createdCount = construction.Count;
            var replacement = sequence.Items.RemoveRange(index, delete).InsertRange(index, inserted);
            if (location.Nested.Length == 0) graph.ReplaceSequence(location.Object, location.Property, replacement);
            else graph.Set(location.Object, location.Property, ShadowJson.Replace(working.Objects[location.Object][location.Property], location.Nested, new ShadowValue.Sequence(replacement)));
            graph.PruneUnreachable();
            working = graph.Build().Apply(working);
            edits.Add(new { id = RequiredString(edit, "id"), path = RequiredString(edit, "path"), index, removed = delete, inserted = inserted.Length, count = replacement.Length });
        }
        var patch = ShadowGraphPatch.Between(snapshot, working);
        token.ThrowIfCancellationRequested();
        var preview = GetBool(args, "preview");
        var result = preview ? patch.Apply(snapshot) : editor.Execute(GetString(args, "description") ?? "Edit drawing collections", patch);
        return new { preview, atomic = true, validation = Validation(preview), resultingRevision = result.Revision, edits, changes = patch.Changes.Length, history = editor.State };
    }

    private static Type? CollectionElementType(ShadowSnapshot snapshot, ShadowJson.Location location)
    {
        // A path through a captured collection resolves to its $items. The public
        // collection runtime type is sufficient even when the parent is not retained.
        var node = snapshot.Objects[location.Object];
        Type? current = typeof(DxfDocument).Assembly.GetType(node.TypeName) ?? typeof(List<>).Assembly.GetType(node.TypeName);
        if (current is null) return null;
        if (location.Property != "$items") current = DxfTypeCatalog.PublicProperties(current).FirstOrDefault(p => p.Name == location.Property)?.ValueType;
        foreach (var segment in location.Nested)
        {
            if (current is null) return null;
            current = ShadowJson.Index(segment, out _) ? Element(current) : DxfTypeCatalog.PublicProperties(current).FirstOrDefault(p => p.Name == segment)?.ValueType;
        }
        return current is null ? null : Element(current);

        static Type? Element(Type type) => type.IsArray ? type.GetElementType() : type.GetInterfaces().Append(type)
            .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    private object Methods(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var type = DxfTypeCatalog.Resolve(GetString(args, "type") ?? (GetString(args, "id") is { } id ? Find(snapshot, new(id)).TypeName : throw new ArgumentException("Provide type or id.")));
        var search = GetString(args, "search");
        var methods = DxfTypeCatalog.PublicMethods(type).Where(m => (search is null || DxfTypeCatalog.MethodSignature(m).Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (!GetBool(args, "callableOnly") || DxfTypeCatalog.IsCallableMethod(m)));
        return new { type = type.FullName, methods = Page(methods, GetInt(args, "offset", 0), GetInt(args, "limit", 100), m => new
        {
            name = m.Name, signature = DxfTypeCatalog.MethodSignature(m), declaringType = m.DeclaringType?.FullName, isStatic = m.IsStatic,
            returnType = m.ReturnType.FullName, generic = m.ContainsGenericParameters, callable = DxfTypeCatalog.IsCallableMethod(m),
            tool = DxfTypeCatalog.IsCallableMethod(m) ? "dxf_method_call" : null,
            reason = DxfTypeCatalog.IsCallableMethod(m) ? "Audited transactional source adapter." : "Metadata only: no audited transactional adapter is registered for this overload.",
            parameters = m.GetParameters().Select(p => new { name = p.Name, type = p.ParameterType.FullName, optional = p.HasDefaultValue, isOut = p.IsOut }).ToArray()
        }) };
    }

    private object CallMethods(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var graph = new ShadowGraphEdit(snapshot);
        var count = 0;
        foreach (var call in args.GetProperty("calls").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var id = new ObjectId(RequiredString(call, "id"));
            var node = Find(snapshot, id);
            var method = RequiredString(call, "method");
            if (node.TypeName != typeof(Ellipse).FullName || method is not (nameof(Ellipse.SetAxis) or "netDxf.Entities.Ellipse.SetAxis(System.Double,System.Double)"))
                throw new NotSupportedException($"{node.TypeName}.{method} has no audited method adapter. Use dxf_methods to discover supported calls.");
            var arguments = call.GetProperty("arguments");
            var first = arguments.GetProperty("axis1").GetDouble(); var second = arguments.GetProperty("axis2").GetDouble();
            // This detached native value applies exactly the same argument validation
            // and normalization as the public method, including branch previews.
            var ellipse = new Ellipse(); ellipse.SetAxis(first, second);
            graph.Set(id, "MajorAxis", ShadowValue.From(ellipse.MajorAxis));
            graph.Set(id, "MinorAxis", ShadowValue.From(ellipse.MinorAxis));
            count++;
        }
        var patch = graph.Build();
        token.ThrowIfCancellationRequested();
        var preview = GetBool(args, "preview");
        var result = preview ? patch.Apply(snapshot) : editor.Execute(GetString(args, "description") ?? "Call drawing methods", patch);
        return new { preview, atomic = true, validation = Validation(preview), resultingRevision = result.Revision, calls = count, changes = patch.Changes.Length, history = editor.State };
    }

    private static string Validation(bool preview) => preview ? "immutable graph only; native source construction/ownership/setter validation runs on apply" : "editor/source transaction";

    private static void ValidateCreatedAttachments(ShadowSnapshot snapshot, ShadowGraphPatch patch, CancellationToken token)
    {
        var added = patch.Changes.Where(c => c.Before is null && c.After is not null).Select(c => c.Id).ToHashSet();
        if (added.Count == 0) return;
        var proposed = patch.Apply(snapshot);
        var visited = new HashSet<ObjectId>();
        var pending = new Stack<ObjectId>(); pending.Push(proposed.Root);
        while (pending.TryPop(out var id))
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(id)) continue;
            added.Remove(id);
            foreach (var reference in proposed.Objects[id].Properties.Values.SelectMany(v => v.References())) pending.Push(reference);
        }
        if (added.Count != 0) throw new ArgumentException($"New source object '{added.OrderBy(id => id.Value, StringComparer.Ordinal).First()}' is not attached to the public document graph. Reference every created resource from an attached object.");
    }

    private sealed class Construction(ShadowGraphEdit graph, Func<string, ObjectId> aliases, CancellationToken token, int count = 0)
    {
        public int Count { get; private set; } = count;
        public void CountObject()
        {
            token.ThrowIfCancellationRequested();
            if (++Count > MaximumCreatedObjects) throw new ArgumentException($"A transaction may create at most {MaximumCreatedObjects} objects, including inline objects.");
        }
        public ImmutableDictionary<string, ShadowValue> Properties(Type type, JsonElement properties) => DxfTypeCatalog.ReadProperties(type, properties, aliases, Inline);
        public ObjectId Create(Type type, JsonElement properties)
        {
            CountObject();
            if (!DxfTypeCatalog.IsSourceConstructible(type)) throw new ArgumentException($"{type.FullName} cannot be constructed as a source object.");
            return graph.Create(type.FullName!, DxfTypeCatalog.Kind(type), Properties(type, properties));
        }
        public ShadowValue Inline(Type type, JsonElement properties) => new ShadowValue.Reference(Create(type, properties));
    }
}
