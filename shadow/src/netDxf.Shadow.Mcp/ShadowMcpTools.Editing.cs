using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private void InstallEditing(McpToolRegistry registry)
    {
        var valueSchema = new { description = "JSON primitive; existing structure patch; {$ref:id}; or typed {kind:atom,type,value}, {kind:structure,type,fields}, {kind:sequence,items}." };
        var propertyEdit = new
        {
            type = "object", properties = Fields(("id", String()), ("path", String(8192)), ("value", valueSchema)),
            required = new[] { "id", "path", "value" }, additionalProperties = false
        };
        Add(registry, "shadow_edit", "Apply up to 256 property/path edits as ONE revision-checked undoable transaction through the existing editor/source adapter. Nested structure edits compose in request order. preview validates the immutable proposal without publishing. Source setters validate/normalize or roll back atomically.", false,
            Fields(("description", String(512)), ("edits", Array(propertyEdit, 1, 256)), ("preview", Boolean())), ["edits"], Edit);
        var operation = new
        {
            type = "object",
            properties = Fields(("op", Enum("create", "addEntity", "set", "replaceCollection", "removeEntities", "removeObject", "pruneUnreachable")),
                ("id", String()), ("alias", String(128)), ("type", String()), ("properties", new { type = "object", maxProperties = 256 }),
                ("owner", String()), ("layout", String()), ("path", String(8192)), ("value", valueSchema), ("items", Array(valueSchema, 0, 10000)), ("ids", Array(String(), 1, 1000))),
            required = new[] { "op" }, additionalProperties = false
        };
        Add(registry, "shadow_graph_edit", "Plan and apply ONE atomic structural editor transaction over ANY linked netDxf graph layer. Operations create/addEntity/set/replaceCollection/removeEntities/removeObject/pruneUnreachable. New nodes can have alias '@name' referenced by later ids or {$ref:'@name'}. Creation properties infer declared CLR types and construct inline objects/arrays; $type selects assignable public subtypes. At most 4096 new objects including inline children. Schema discovery lists constructor requirements. Ordered operations must maintain valid references. Native source adapter owns construction, collection ownership, validation, rollback and undo. preview checks the immutable graph only.", false,
            Fields(("description", String(512)), ("operations", Array(operation, 1, 256)), ("preview", Boolean())), ["operations"], GraphEdit);
        Add(registry, "shadow_undo", "Undo the last transaction in the active workspace, preserving native source synchronization and revision checks.", false, [], [],
            (a, e, s, ct) => { ct.ThrowIfCancellationRequested(); var after = e.Undo(); return new { changed = !ReferenceEquals(s, after), history = e.State }; });
        Add(registry, "shadow_redo", "Redo the last undone transaction in the active workspace through the existing editor/source adapter.", false, [], [],
            (a, e, s, ct) => { ct.ThrowIfCancellationRequested(); var after = e.Redo(); return new { changed = !ReferenceEquals(s, after), history = e.State }; });
        Add(registry, "shadow_select", "Replace or toggle the editor's occurrence selection. Discover rendered occurrence keys through Studio scene/state tools. Selection does not change the model revision.", false,
            Fields(("keys", Array(String(), 0, 10000)), ("toggle", Boolean())), ["keys"], (a, e, s, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var keys = a.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToArray();
                if (GetBool(a, "toggle")) foreach (var key in keys) e.ToggleSelection(key); else e.Select(keys);
                return new { selection = e.State.Selection };
            });
    }

    private object Edit(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var working = snapshot;
        var changed = new HashSet<(ObjectId Object, string Property)>();
        foreach (var edit in args.GetProperty("edits").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var location = ShadowJson.Locate(working, new(RequiredString(edit, "id")), RequiredString(edit, "path"));
            var node = working.Objects[location.Object];
            if (!node.WritableProperties.Contains(location.Property))
                throw new ArgumentException($"{node.Id}/{ShadowJson.Escape(location.Property)} is not a writable property. Collection membership and structural updates use shadow_graph_edit.");
            var replacement = ShadowJson.FromInput(edit.GetProperty("value"), location.Value);
            var after = ShadowJson.Replace(node[location.Property], location.Nested, replacement);
            working = working.Apply(ShadowEditBatch.Create(working, new ShadowPropertyEdit(location.Object, location.Property, node[location.Property], after)));
            changed.Add((location.Object, location.Property));
        }
        var changes = changed.OrderBy(e => e.Object.Value, StringComparer.Ordinal).ThenBy(e => e.Property, StringComparer.Ordinal)
            .Select(e => new ShadowPropertyEdit(e.Object, e.Property, snapshot.Objects[e.Object][e.Property], working.Objects[e.Object][e.Property]))
            .Where(e => !ShadowValue.Equivalent(e.Before, e.After)).ToImmutableArray();
        var batch = new ShadowEditBatch(snapshot.DocumentId, snapshot.Revision, changes);
        token.ThrowIfCancellationRequested();
        var preview = GetBool(args, "preview");
        var result = preview ? snapshot.Apply(batch) : editor.Execute(GetString(args, "description") ?? "Agent property edit", batch);
        return new
        {
            preview, validation = preview ? "immutable graph only; native source setter validation runs on apply" : "editor/source transaction",
            resultingRevision = result.Revision, edits = changes.Length,
            changes = changes.Select(e => new { id = e.Object.Value, path = "/" + ShadowJson.Escape(e.Property), value = ShadowJson.Describe(result.Objects[e.Object][e.Property], 2, 8, textLimit: 256) }).ToArray()
        };
    }

    private object GraphEdit(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        var working = snapshot;
        var aliases = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var operationCount = 0;
        var createdCount = 0;
        foreach (var operation in args.GetProperty("operations").EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var op = RequiredString(operation, "op"); var graphEdit = new ShadowGraphEdit(working);
            switch (op)
            {
                case "create":
                case "addEntity":
                    var type = DxfTypeCatalog.Resolve(RequiredString(operation, "type"));
                    if (!DxfTypeCatalog.IsSourceConstructible(type)) throw new ArgumentException($"{type.FullName} cannot be constructed as a source object.");
                    var construction = new Construction(graphEdit, Resolve, token, createdCount);
                    construction.CountObject();
                    var properties = construction.Properties(type, operation.TryGetProperty("properties", out var supplied) ? supplied : McpJson.EmptyObject);
                    createdCount = construction.Count;
                    ObjectId created;
                    if (op == "addEntity")
                    {
                        if (!typeof(netDxf.Entities.EntityObject).IsAssignableFrom(type)) throw new ArgumentException("addEntity requires a netDxf entity type.");
                        var owner = GetString(operation, "owner") is { } ownerName ? Resolve(ownerName) :
                            working.Layout(GetString(operation, "layout") ?? "Model")["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown layout.");
                        created = graphEdit.AddEntity(owner, type.FullName!, properties);
                    }
                    else created = graphEdit.Create(type.FullName!, DxfTypeCatalog.Kind(type), properties);
                    if (GetString(operation, "alias") is { } alias)
                    {
                        if (!alias.StartsWith('@') || alias.Length < 2 || !aliases.TryAdd(alias, created))
                            throw new ArgumentException("Each creation alias must be unique and start with '@'.");
                    }
                    break;
                case "set":
                    var location = ShadowJson.Locate(working, Resolve(RequiredString(operation, "id")), RequiredString(operation, "path"));
                    if (!operation.TryGetProperty("value", out var value)) throw new ArgumentException("set requires value.");
                    graphEdit.Set(location.Object, location.Property, ShadowJson.Replace(working.Objects[location.Object][location.Property], location.Nested,
                        ShadowJson.FromInput(value, location.Value, Resolve)));
                    break;
                case "replaceCollection":
                    var selected = ShadowJson.Locate(working, Resolve(RequiredString(operation, "id")), RequiredString(operation, "path"));
                    if (!operation.TryGetProperty("items", out var items)) throw new ArgumentException("replaceCollection requires items.");
                    if (selected.Nested.Length != 0) throw new ArgumentException("Use set with an array for an embedded sequence, or address its owning object's collection property.");
                    graphEdit.ReplaceSequence(selected.Object, selected.Property, items.EnumerateArray().Select(v => ShadowJson.FromInput(v, null, Resolve)));
                    break;
                case "removeEntities":
                    if (!operation.TryGetProperty("ids", out var ids)) throw new ArgumentException("removeEntities requires ids.");
                    graphEdit.RemoveEntities(ids.EnumerateArray().Select(v => Resolve(v.GetString()!)));
                    break;
                case "removeObject":
                    var remove = Resolve(RequiredString(operation, "id")); var old = Find(working, remove);
                    working = new ShadowGraphPatch(working.DocumentId, working.Revision, [new(remove, old, null)]).Apply(working);
                    operationCount++;
                    continue;
                case "pruneUnreachable": graphEdit.PruneUnreachable(); break;
                default: throw new ArgumentException($"Unknown graph operation '{op}'.");
            }
            working = graphEdit.Build().Apply(working);
            operationCount++;
        }
        var patch = ShadowGraphPatch.Between(snapshot, working);
        token.ThrowIfCancellationRequested();
        ValidateCreatedAttachments(snapshot, patch, token);
        var preview = GetBool(args, "preview");
        var result = preview ? patch.Apply(snapshot) : editor.Execute(GetString(args, "description") ?? "Agent structural edit", patch);
        return new
        {
            preview, validation = preview ? "immutable graph only; native source construction and ownership validation runs on apply" : "editor/source transaction",
            operations = operationCount, changes = patch.Changes.Length, resultingRevision = result.Revision,
            aliases = aliases.ToDictionary(p => p.Key, p => p.Value.Value),
            added = patch.Changes.Count(c => c.Before is null && c.After is not null),
            removed = patch.Changes.Count(c => c.Before is not null && c.After is null),
            history = editor.State
        };

        ObjectId Resolve(string id) => id.StartsWith('@') ? aliases.TryGetValue(id, out var alias) ? alias : throw new ArgumentException($"Unknown transaction alias '{id}'.") : new(id);
    }
}
