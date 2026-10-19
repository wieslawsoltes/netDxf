using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    // Only immutable presentation metadata is retained. Selection, history, workspace
    // receipts, font revisions and retained snapshots are still read on each request.
    // Exact snapshot/scene references include source edits and compiled resource changes;
    // keeping one entry bounds retention when documents or layouts are replaced.
    private StatePresentation? statePresentation;
    private CadPresentation? cadPresentation;

    private readonly record struct StateItem(string Key, string Id, string Type, string Layer, bool Inserted);
    private sealed record StateDiagnostic(string Code, string Message, string? Id, string Severity);
    private sealed record StatePresentation(ShadowSnapshot Snapshot, RenderScene Scene,
        ImmutableArray<string> Layouts, ImmutableArray<StateItem> Items, ImmutableArray<StateDiagnostic> Diagnostics);
    private readonly record struct CadItem(string Id, string Key, string Type, string Layer, int Count, Bounds2 Bounds);
    private sealed record CadBlock(string Id, string Name, int Entities, Vec3 Origin, string Description, int Attributes, string Units,
        bool IsExternal, bool ScaleUniformly, bool AllowExploding);
    private sealed record CadPresentation(ShadowSnapshot Snapshot, RenderScene Scene,
        ImmutableArray<CadItem> Items, int ItemCount, ImmutableArray<DraftingLayer> Layers,
        ImmutableArray<CadBlock> Blocks, ImmutableArray<string> Linetypes);

    private StatePresentation StateMetadata(ShadowSnapshot snapshot, RenderScene scene)
    {
        if (statePresentation is { } cached && ReferenceEquals(cached.Snapshot, snapshot) && ReferenceEquals(cached.Scene, scene))
            return cached;
        return statePresentation = new(snapshot, scene,
            snapshot.Layouts.Select(l => l["Name"].Text).ToImmutableArray(),
            scene.Items.Take(2000).Select(i => new StateItem(i.Key, i.Entity.Value, snapshot.Objects[i.Entity].Type,
                i.Style.Layer, !i.InsertPath.IsEmpty)).ToImmutableArray(),
            scene.Diagnostics.Take(200).Select(d => new StateDiagnostic(d.Code, d.Message, d.Object?.Value,
                d.Severity.ToString())).ToImmutableArray());
    }

    private CadPresentation CadMetadata(ShadowSnapshot snapshot, RenderScene scene)
    {
        if (cadPresentation is { } cached && ReferenceEquals(cached.Snapshot, snapshot) && ReferenceEquals(cached.Scene, scene))
            return cached;

        // Preserve first-occurrence order and all occurrence bounds without allocating a
        // grouping and backing array for every root. Only the existing 5,000 display rows
        // need aggregate storage; the complete distinct-root count remains available.
        var roots = new Dictionary<ObjectId, int>();
        var rows = new List<CadItem>(Math.Min(5000, scene.Items.Length));
        foreach (var item in scene.Items)
        {
            var root = DraftingQueries.Root(item);
            if (roots.TryGetValue(root, out var index))
            {
                if (index >= 0)
                {
                    var row = rows[index];
                    rows[index] = row with { Count = checked(row.Count + 1), Bounds = row.Bounds.Union(item.Bounds) };
                }
                continue;
            }
            index = rows.Count < 5000 ? rows.Count : -1;
            roots.Add(root, index);
            if (index >= 0)
            {
                var source = snapshot.Read(root);
                rows.Add(new(root.Value, item.Key, source.Type, source["Layer"]["Name"].Text, 1, item.Bounds));
            }
        }
        return cadPresentation = new(snapshot, scene, rows.ToImmutableArray(), roots.Count, DraftingLayers.Read(snapshot),
            snapshot.Document["Blocks"].Items.Where(b => !b["Name"].Text.StartsWith('*')).Take(1024)
                .Select(b => new CadBlock(b.Id!.Value.Value, b["Name"].Text, b["Entities"].Items.Count(), b["Origin"].Vector(),
                    b["Description"].Text, b["AttributeDefinitions"].Items.Count(), b["Record"]["Units"].Text, b["IsXRef"].Boolean(),
                    b["Record"]["ScaleUniformly"].Boolean(), b["Record"]["AllowExploding"].Boolean(true))).ToImmutableArray(),
            snapshot.Document["Linetypes"].Items.Select(l => l["Name"].Text).ToImmutableArray());
    }

    /// <summary>Return a full current receipt only when the caller's monotonic server
    /// receipt is stale. An unchanged poll does no geometry, grouping or serialization work.</summary>
    public object? CadStateIfChanged(string? knownServer, long? knownChange)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (knownChange < 0) throw new ArgumentOutOfRangeException(nameof(knownChange));
            return knownServer == serverId && knownChange == changeVersion ? null : CadState();
        }
    }
}
