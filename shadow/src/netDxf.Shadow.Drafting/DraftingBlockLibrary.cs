using System.Collections.Immutable;

namespace netDxf.Shadow.Drafting;

public enum BlockLibraryConflict { Reject, Reuse, Rename, Redefine }
public enum BlockLibraryResourceConflict { Reuse, Rename, Reject }
public sealed record BlockLibraryImport(ObjectId SourceBlock, string Name,
    BlockLibraryConflict BlockConflict = BlockLibraryConflict.Reject,
    BlockLibraryResourceConflict ResourceConflict = BlockLibraryResourceConflict.Reuse,
    bool PreserveLayerZero = false);
public sealed record BlockLibraryLimits(int MaximumObjects = 100_000, int MaximumValues = 2_000_000, int MaximumValueDepth = 64);
public sealed record BlockLibraryResource(ObjectId Source, ObjectId Target, string Table,
    string SourceName, string TargetName, string Action);
public sealed record BlockLibraryPlan(DraftingPlan Edit, ObjectId Block,
    ImmutableArray<BlockLibraryResource> Resources, ImmutableDictionary<ObjectId, ObjectId> Identities);

/// <summary>Copies a definition dependency graph between immutable drawings. No live
/// source objects, files or application state are retained in the resulting plan.</summary>
public static class DraftingBlockLibrary
{
    private static readonly string[] Tables = ["Blocks", "Layers", "Linetypes", "TextStyles", "ShapeStyles",
        "DimensionStyles", "MlineStyles", "ApplicationRegistries", "ImageDefinitions",
        "UnderlayDgnDefinitions", "UnderlayDwfDefinitions", "UnderlayPdfDefinitions", "UCSs", "Views"];

    public static BlockLibraryPlan Import(ShadowSnapshot target, ShadowSnapshot source, BlockLibraryImport request,
        BlockLibraryLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(request);
        limits ??= new();
        if (limits.MaximumObjects < 1 || limits.MaximumValues < 1 || limits.MaximumValueDepth is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(limits));
        if (!Enum.IsDefined(request.BlockConflict) || !Enum.IsDefined(request.ResourceConflict)) throw new ArgumentException("Unknown library conflict policy.");
        cancellationToken.ThrowIfCancellationRequested();
        var root = source.Read(request.SourceBlock);
        if (root.Object?.Kind != ShadowObjectKind.Block || !source.Document["Blocks"].Items.Any(b => b.Id == request.SourceBlock))
            throw new ArgumentException("Choose a block from the source drawing.");
        var model = source.Layout("Model")["AssociatedBlock"].Id == request.SourceBlock;
        if (!model && root["Record"]["Layout"].Exists) throw new ArgumentException("Import model space or a named definition, not a paper-space layout.");
        var name = request.Name?.Trim() ?? "";
        var existing = target.Document["Blocks"].Items.FirstOrDefault(b => b["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        DraftingBlocks.ValidateName(target, name, existing.Id);
        if (existing.Exists && request.BlockConflict == BlockLibraryConflict.Reject) throw new ArgumentException("A block with that name already exists; choose an explicit conflict policy.");
        if (existing.Exists && request.BlockConflict == BlockLibraryConflict.Reuse)
        {
            var reused = existing.Id!.Value;
            return new(new("Reuse block " + name, new ShadowGraphEdit(target).Build(), []), reused,
                [new(request.SourceBlock, reused, "Blocks", root["Name"].Text, existing["Name"].Text, "Reuse")],
                ImmutableDictionary<ObjectId, ObjectId>.Empty.Add(request.SourceBlock, reused));
        }
        bool redefine = existing.Exists && request.BlockConflict == BlockLibraryConflict.Redefine;
        if (redefine)
        {
            DraftingBlocks.Definition(target, existing.Id!.Value);
            if (root["IsXRef"].Boolean()) throw new ArgumentException("A local definition cannot be redefined as an external reference.");
        }
        var resources = new Dictionary<ObjectId, string>(); var map = new Dictionary<ObjectId, ObjectId>();
        var reusedBlocks = new Dictionary<ObjectId, ObjectId>(); var absentDefinitions = new HashSet<ObjectId>();
        var copied = new HashSet<ObjectId>(); var pending = new Queue<ObjectId>();
        var renamed = new Dictionary<ObjectId, string>(); var reports = ImmutableArray.CreateBuilder<BlockLibraryResource>();
        var additions = Tables.ToDictionary(t => t, _ => new List<ObjectId>());
        var targetNames = new Dictionary<string, Dictionary<string, ObjectId>>(); var occupied = new Dictionary<string, HashSet<string>>();
        var incomingNames = Tables.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var values = 0; var indexed = 0;
        foreach (var table in Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var names = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in target.Document[table].Items)
            {
                if (++indexed > limits.MaximumObjects) throw new InvalidOperationException("Library resource index budget exceeded.");
                names.Add(item["Name"].Text, item.Id!.Value);
            }
            targetNames.Add(table, names); occupied.Add(table, names.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (source.Document[table].Id is { } from && target.Document[table].Id is { } to) map.Add(from, to);
            foreach (var item in source.Document[table].Items)
            {
                if (++indexed > limits.MaximumObjects) throw new InvalidOperationException("Library resource index budget exceeded.");
                resources.Add(item.Id!.Value, table);
                incomingNames[table].Add(item["Name"].Text);
            }
        }
        if (existing.Exists && request.BlockConflict == BlockLibraryConflict.Rename) name = Unique("Blocks", name);
        renamed[request.SourceBlock] = name;
        if (redefine)
        {
            BindReplacement(request.SourceBlock, existing.Id!.Value);
            foreach (var property in new[] { "Record", "Entities", "AttributeDefinitions", "EndRecord" })
                if (root[property].Id is { } from && existing[property].Id is { } to) BindReplacement(from, to);
            var oldDefinitions = DefinitionEntries(existing).ToDictionary(d => d["Tag"].Text, d => d.Id!.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var definition in DefinitionEntries(root))
                if (oldDefinitions.TryGetValue(definition["Tag"].Text, out var old)) BindReplacement(definition.Id!.Value, old);
            reports.Add(new(request.SourceBlock, existing.Id!.Value, "Blocks", root["Name"].Text, name, "Redefine"));
        }
        else Allocate(request.SourceBlock);
        while (pending.TryDequeue(out var id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = source.Objects[id];
            foreach (var property in node.Properties)
            {
                if (property.Key is "Owner" or "Handle" || IsLayoutLink(id, property.Key)) continue;
                if (node.TypeName == "netDxf.Entities.Attribute" && property.Key == "Definition" && property.Value is ShadowValue.Reference definition)
                {
                    BindReusedDefinition(definition.Id);
                    if (absentDefinitions.Contains(definition.Id)) continue;
                }
                Visit(property.Value, reference => Allocate(reference));
            }
        }
        // Foreign entity dependencies must not be silently dragged out of another
        // definition/layout. Ownership is followed only within the chosen closure.
        foreach (var id in copied)
        {
            var node = source.Objects[id];
            if (node["Owner"] is ShadowValue.Reference owner && !map.ContainsKey(owner.Id))
                throw new NotSupportedException($"{node.Type} has an owner outside the imported definition graph.");
            if (node.Type == "XDataRecord" && source.Read(id)["Code"].Text == "DatabaseHandle" && source.Read(id)["Value"].Text is not ("" or "0"))
                throw new NotSupportedException("Library transfer requires explicit remapping for handle-valued XData.");
        }
        var edit = new ShadowGraphEdit(target);
        var replacementIds = map.Values.ToHashSet();
        var recordBlocks = copied.Where(id => source.Objects[id].Kind == ShadowObjectKind.Block && source.Read(id)["Record"].Id is not null)
            .ToDictionary(id => source.Read(id)["Record"].Id!.Value, id => id);
        if (redefine)
        {
            edit.RemoveEntities(existing["Entities"].Items.Select(e => e.Id!.Value));
            foreach (var old in DefinitionEntries(existing))
                if (!replacementIds.Contains(old.Id!.Value)) edit.Set(old.Id!.Value, "Owner", ShadowValue.Null);
        }
        foreach (var id in copied)
        {
            cancellationToken.ThrowIfCancellationRequested(); var node = source.Objects[id];
            var properties = node.Properties.Where(p => p.Key != "Handle" && !IsLayoutLink(id, p.Key))
                .ToDictionary(p => p.Key, p => node.TypeName == "netDxf.Entities.Attribute" && p.Key == "Definition" &&
                    p.Value is ShadowValue.Reference definition && absentDefinitions.Contains(definition.Id) ? ShadowValue.Null : Remap(p.Value, 0), StringComparer.Ordinal);
            if (renamed.TryGetValue(id, out var importedName))
            {
                properties["Name"] = ShadowValue.From(importedName);
                if (properties.ContainsKey("IsReserved")) properties["IsReserved"] = ShadowValue.From(false);
            }
            if (id == request.SourceBlock)
            {
                properties["IsForInternalUseOnly"] = ShadowValue.From(false); properties["IsReserved"] = ShadowValue.From(false);
                if (properties.GetValueOrDefault("Flags") is ShadowValue.Atom flags)
                {
                    var parts = flags.Text.Split(',').Select(p => p.Trim()).Where(p => p != "AnonymousBlock").ToArray();
                    properties["Flags"] = flags with { Text = long.TryParse(flags.Text, out var bits) ? (bits & ~1L).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : parts.Length == 0 ? "None" : string.Join(", ", parts) };
                }
                if (model) properties["Origin"] = source.Document["DrawingVariables"]["InsBase"].Value;
            }
            if (recordBlocks.TryGetValue(id, out var block) && renamed.TryGetValue(block, out var blockName))
                {
                    properties["Name"] = ShadowValue.From(blockName);
                    if (block == request.SourceBlock && model)
                    {
                        properties["Layout"] = ShadowValue.Null;
                        properties["Units"] = source.Document["DrawingVariables"]["InsUnits"].Value;
                        properties["IsForInternalUseOnly"] = ShadowValue.From(false);
                    }
                }
            var destination = map[id];
            if (target.Objects.ContainsKey(destination))
            {
                foreach (var (property, value) in properties) edit.Set(destination, property, value);
            }
            else edit.Create(node.TypeName, node.Kind, properties, destination);
        }
        foreach (var table in Tables)
            if (additions[table].Count != 0 || redefine && table == "Blocks")
            {
                var members = target.Document[table].Items.Select(r => r.Value)
                    .Concat(additions[table].Select(id => (ShadowValue)new ShadowValue.Reference(map[id]))).ToImmutableArray();
                edit.ReplaceSequence(target.Root, table, members);
                var collection = target.Document[table];
                if (collection["Items"].Value is ShadowValue.Sequence) edit.Set(collection.Id!.Value, "Items", new ShadowValue.Sequence(members));
                if (collection["Names"].Value is ShadowValue.Sequence)
                {
                    var names = target.Document[table].Items.Select(r => (ShadowValue)ShadowValue.From(redefine && r.Id == existing.Id ? name : r["Name"].Text))
                        .Concat(additions[table].Select(id => ShadowValue.From(renamed.GetValueOrDefault(id, source.Read(id)["Name"].Text)))).ToImmutableArray();
                    edit.Set(collection.Id!.Value, "Names", new ShadowValue.Sequence(names));
                }
            }
        edit.PruneUnreachable();
        var patch = edit.Build(); var proposed = patch.Apply(target);
        ValidateReferences(proposed, map[request.SourceBlock], limits.MaximumObjects, cancellationToken);
        return new(new("Import block " + name, patch, []), map[request.SourceBlock], reports.ToImmutable(), map.ToImmutableDictionary());

        void BindReplacement(ObjectId from, ObjectId to)
        {
            if (copied.Count >= limits.MaximumObjects) throw new InvalidOperationException("Library object budget exceeded.");
            map.Add(from, to); if (copied.Add(from)) pending.Enqueue(from);
            // XDATA dictionaries are constructor-owned. Retained definitions must
            // update their membership rather than claim a different dictionary.
            if (source.Read(from)["XData"].Id is { } sourceData && target.Read(to)["XData"].Id is { } targetData)
                BindReplacement(sourceData, targetData);
        }
        bool IsLayoutLink(ObjectId id, string property) => model && root["Record"].Id == id && property == "Layout";
        string Unique(string table, string requested)
        {
            if (table == "Blocks" && requested.StartsWith('*'))
            {
                var prefix = requested.Length >= 2 ? requested[..2] : "*U";
                for (int index = 1; index <= limits.MaximumObjects; index++)
                {
                    var candidate = prefix + index;
                    if (!occupied[table].Contains(candidate) && !incomingNames[table].Contains(candidate)) return candidate;
                }
                throw new InvalidOperationException("Anonymous block naming budget exceeded.");
            }
            var stem = requested.Length > 230 ? requested[..230] : requested;
            for (int index = 1; index <= limits.MaximumObjects; index++)
            {
                var candidate = stem + "$" + index;
                if (!occupied[table].Contains(candidate) && !incomingNames[table].Contains(candidate)) return candidate;
            }
            throw new InvalidOperationException("No unique resource name is available within the library budget.");
        }
        void Allocate(ObjectId id)
        {
            if (map.ContainsKey(id)) return;
            if (!source.Objects.TryGetValue(id, out var node)) throw new ArgumentException("The library graph contains a dangling reference.");
            if (node.TypeName == "netDxf.Entities.AttributeDefinition")
            {
                BindReusedDefinition(id);
                if (map.ContainsKey(id)) return;
                if (absentDefinitions.Contains(id)) throw new NotSupportedException("A non-attribute dependency references a definition absent from a reused block.");
            }
            if ((node.Kind & (ShadowObjectKind.Document | ShadowObjectKind.Layout)) != 0)
                throw new NotSupportedException("A library dependency escapes into document or layout state.");
            if (copied.Count >= limits.MaximumObjects) throw new InvalidOperationException("Library object budget exceeded.");
            string? table = resources.GetValueOrDefault(id); string? resourceName = null; string action = "Import";
            if (table is not null)
            {
                resourceName = renamed.GetValueOrDefault(id, source.Read(id)["Name"].Text);
                bool anonymous = table == "Blocks" && resourceName.StartsWith('*');
                if (anonymous && occupied[table].Contains(resourceName))
                {
                    resourceName = Unique(table, resourceName); action = "Rename"; renamed[id] = resourceName;
                }
                else if (targetNames[table].TryGetValue(resourceName, out var collision))
                {
                    bool special = table == "ApplicationRegistries" || table == "Layers" && resourceName == "0" ||
                        table == "Linetypes" && (resourceName.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || resourceName.Equals("ByBlock", StringComparison.OrdinalIgnoreCase));
                    if (table == "Layers" && resourceName == "0" && request.PreserveLayerZero)
                    {
                        BindReplacement(id, collision);
                        reports.Add(new(id, collision, table, resourceName, resourceName, "Redefine")); return;
                    }
                    if (id != request.SourceBlock && (special || request.ResourceConflict == BlockLibraryResourceConflict.Reuse))
                    {
                        if (table == "Blocks") reusedBlocks.Add(id, collision);
                        map.Add(id, collision); reports.Add(new(id, collision, table, source.Read(id)["Name"].Text, target.Read(collision)["Name"].Text, "Reuse")); return;
                    }
                    if (request.ResourceConflict == BlockLibraryResourceConflict.Reject) throw new ArgumentException($"Resource '{table}/{resourceName}' already exists.");
                    resourceName = Unique(table, resourceName); action = "Rename"; renamed[id] = resourceName;
                }
                else if (occupied[table].Contains(resourceName))
                {
                    resourceName = Unique(table, resourceName); action = "Rename"; renamed[id] = resourceName;
                }
                occupied[table].Add(resourceName); additions[table].Add(id);
            }
            var next = new ObjectId("N:" + Guid.NewGuid().ToString("N")); map.Add(id, next); copied.Add(id); pending.Enqueue(id);
            if (table is not null) reports.Add(new(id, next, table, source.Read(id)["Name"].Text, resourceName!, action));
        }
        void BindReusedDefinition(ObjectId definition)
        {
            if (source.Read(definition)["Owner"].Id is not { } owner || resources.GetValueOrDefault(owner) != "Blocks") return;
            Allocate(owner);
            if (!reusedBlocks.TryGetValue(owner, out var reused)) return;
            var tag = source.Read(definition)["Tag"].Text;
            var replacement = DefinitionEntries(target.Read(reused)).FirstOrDefault(d => d["Tag"].Text.Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (replacement.Id is { } id) map.TryAdd(definition, id);
            else absentDefinitions.Add(definition);
        }
        void Visit(ShadowValue value, Action<ObjectId> reference)
        {
            var stack = new Stack<(ShadowValue Value, int Depth)>(); stack.Push((value, 0));
            while (stack.TryPop(out var entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++values > limits.MaximumValues || entry.Depth > limits.MaximumValueDepth) throw new InvalidOperationException("Library value budget exceeded.");
                switch (entry.Value)
                {
                    case ShadowValue.Reference r: reference(r.Id); break;
                    case ShadowValue.Sequence sequence: foreach (var item in sequence.Items) stack.Push((item, entry.Depth + 1)); break;
                    case ShadowValue.Structure structure: foreach (var field in structure.Fields.Values) stack.Push((field, entry.Depth + 1)); break;
                    case ShadowValue.Fault: throw new NotSupportedException("Unreadable library properties cannot be transferred.");
                }
            }
        }
        ShadowValue Remap(ShadowValue value, int depth)
        {
            if (depth > limits.MaximumValueDepth) throw new InvalidOperationException("Library value nesting budget exceeded.");
            return value switch
            {
                ShadowValue.Reference r => new ShadowValue.Reference(map[r.Id]),
                ShadowValue.Sequence s => new ShadowValue.Sequence(s.Items.Select(v => Remap(v, depth + 1)).ToImmutableArray()),
                ShadowValue.Structure s => s with { Fields = s.Fields.ToImmutableDictionary(p => p.Key, p => Remap(p.Value, depth + 1), StringComparer.Ordinal) },
                _ => value
            };
        }
    }

    private static IEnumerable<ShadowReader> DefinitionEntries(ShadowReader block) =>
        block["AttributeDefinitions"].Items.Select(entry => entry["Value"]);

    private static void ValidateReferences(ShadowSnapshot snapshot, ObjectId root, int maximumVisits, CancellationToken token)
    {
        var active = new HashSet<ObjectId>(); var complete = new HashSet<ObjectId>();
        var pending = new Stack<(ObjectId Id, int Depth, bool Exit)>(); pending.Push((root, 0, false)); var visits = 0;
        while (pending.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            if (entry.Exit) { active.Remove(entry.Id); complete.Add(entry.Id); continue; }
            if (complete.Contains(entry.Id)) continue;
            if (++visits > maximumVisits || entry.Depth > 64) throw new InvalidOperationException("Library block-reference budget exceeded.");
            if (!active.Add(entry.Id)) throw new ArgumentException("Import would create a circular block reference.");
            var block = snapshot.Read(entry.Id);
            if (block.Object?.Kind != ShadowObjectKind.Block) throw new ArgumentException("An imported INSERT references a missing definition.");
            pending.Push((entry.Id, entry.Depth, true));
            foreach (var entity in block["Entities"].Items)
            {
                if (++visits > maximumVisits) throw new InvalidOperationException("Library block-reference budget exceeded.");
                if (entity.Type == "Insert" && entity["Block"].Id is { } child) pending.Push((child, entry.Depth + 1, false));
            }
        }
    }
}
