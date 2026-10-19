namespace netDxf.Shadow.Health;

public static partial class DrawingHealth
{
    private sealed partial class Analyzer
    {
        private void CheckResources()
        {
            if (resources.AvailableShxFonts is { } inventory && inventory.Count > options.MaximumObjects) throw new BudgetReached();
            var availableFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (resources.AvailableShxFonts is { } supplied) foreach (var alias in supplied) { Tick(); availableFonts.Add(alias); }
            var tableNames = new[] { "Blocks", "Layers", "Linetypes", "TextStyles", "ShapeStyles" };
            var tableIds = tableNames.Select(t => snapshot.Document[t].Id).Where(i => i.HasValue).Select(i => i!.Value).ToHashSet();
            var blockUsers = new HashSet<ObjectId>();
            var fontReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in snapshot.Objects.Values)
            {
                Tick(); var reader = snapshot.Read(node.Id);
                if (node.Type == "Insert" && reader["Block"].Id is { } block) blockUsers.Add(block);
                if (node.Kind.HasFlag(ShadowObjectKind.Layout) && reader["AssociatedBlock"].Id is { } layoutBlock) blockUsers.Add(layoutBlock);
            }
            foreach (var (table, header) in new[] { ("Layers", "CLayer"), ("Linetypes", "CeLtype"), ("TextStyles", "TextStyle") })
            {
                Tick(); var current = snapshot.Document["DrawingVariables"][header].Text;
                if (current.Length > 0 && !snapshot.Document[table].Items.Any(r => r["Name"].Text.Equals(current, StringComparison.OrdinalIgnoreCase)))
                    Add("resource.current", DiagnosticSeverity.Error, snapshot.Root, "DrawingVariables." + header, "Current resource '" + current + "' is absent from " + table + ".");
            }
            foreach (var table in tableNames)
                foreach (var resource in snapshot.Document[table].Items.OrderBy(r => r.Id?.Value, StringComparer.Ordinal))
                {
                    Tick(); if (resource.Id is not { } id) continue;
                    var name = resource["Name"].Text;
                    var current = table switch { "Linetypes" => snapshot.Document["DrawingVariables"]["CeLtype"].Text, "TextStyles" => snapshot.Document["DrawingVariables"]["TextStyle"].Text, _ => "" };
                    var reserved = resource["IsReserved"].Boolean() || name.StartsWith('*') || table == "TextStyles" && name.Equals("Standard", StringComparison.OrdinalIgnoreCase)
                        || table == "Linetypes" && new[] { "ByLayer", "ByBlock", "Continuous" }.Contains(name, StringComparer.OrdinalIgnoreCase);
                    var used = table == "Blocks" ? blockUsers.Contains(id) : users.TryGetValue(id, out var refs) && refs.Any(r => r != id && r != snapshot.Root && !tableIds.Contains(r));
                    if (options.FindUnusedResources && table != "Layers" && !reserved && !used && !name.Equals(current, StringComparison.OrdinalIgnoreCase))
                        Add("resource.unused-" + (table == "Blocks" ? "block" : table == "Linetypes" ? "linetype" : "font-style"), DiagnosticSeverity.Information, id, "Name", "Unused " + resource.Type + " definition '" + name + "'. No deletion was performed.");
                    if (table is "TextStyles" or "ShapeStyles")
                    {
                        foreach (var property in table == "TextStyles" ? new[] { "FontFile", "BigFont" } : new[] { "File" })
                        {
                            Tick(); var file = resource[property].Text;
                            fontReferences.Add(file); fontReferences.Add(file[(Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\')) + 1)..]);
                            if (!file.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || resources.AvailableShxFonts is null) continue;
                            var basename = file[(Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\')) + 1)..];
                            if (!availableFonts.Contains(file) && !availableFonts.Contains(basename))
                                Add("resource.missing-shx", DiagnosticSeverity.Warning, id, property, "SHX reference '" + file + "' is absent from the supplied host resource inventory.");
                        }
                    }
                }
            if (options.FindUnusedResources && resources.AvailableShxFonts is { } fonts)
                foreach (var font in fonts.Order(StringComparer.OrdinalIgnoreCase))
                {
                    Tick(); if (!fontReferences.Contains(font)) Add("resource.unused-font", DiagnosticSeverity.Information, null, font,
                        "Host SHX alias '" + font + "' has no matching document font reference. It may still be used as a fallback; no resource was removed.");
                }
        }
        private void CheckBlockCycles()
        {
            var graph = new Dictionary<ObjectId, List<ObjectId>>();
            foreach (var block in snapshot.Objects.Values.Where(n => n.Kind.HasFlag(ShadowObjectKind.Block)).OrderBy(n => n.Id.Value, StringComparer.Ordinal))
            {
                Tick(); var edges = new List<ObjectId>(); graph[block.Id] = edges;
                foreach (var entity in snapshot.Read(block.Id)["Entities"].Items)
                { Tick(); if (entity.Type == "Insert" && entity["Block"].Id is { } next) edges.Add(next); }
                edges.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            }
            var colors = new Dictionary<ObjectId, int>();
            foreach (var start in graph.Keys.OrderBy(i => i.Value, StringComparer.Ordinal))
            {
                if (colors.ContainsKey(start)) continue;
                var stack = new Stack<(ObjectId Id, int Index)>(); stack.Push((start, 0)); colors[start] = 1;
                while (stack.Count > 0)
                {
                    Tick(); var (id, index) = stack.Pop(); var edges = graph.GetValueOrDefault(id);
                    if (edges is null || index >= edges.Count) { colors[id] = 2; continue; }
                    stack.Push((id, index + 1)); var next = edges[index]; var color = colors.GetValueOrDefault(next);
                    if (color == 1) Add("block.recursion", DiagnosticSeverity.Error, id, "Entities", "Recursive INSERT definitions cannot be expanded finitely.", [next]);
                    else if (color == 0) { colors[next] = 1; stack.Push((next, 0)); }
                }
            }
        }
    }
}
