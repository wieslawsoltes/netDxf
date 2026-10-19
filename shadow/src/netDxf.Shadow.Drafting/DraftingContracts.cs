using System.Collections.Immutable;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Drafting;

/// <summary>Detached command input. Distances use drawing units; angles use degrees.</summary>
public sealed record DraftingCommand(string Name, ImmutableArray<Vec3> Points = default,
    ImmutableArray<ObjectId> Entities = default, double Value = 0, double Secondary = 0,
    string? Text = null, string Layer = "0", bool Closed = false, int Count = 6, double Rotation = 0,
    ObjectId? Reference = null, Vec3? Scale = null, Vec3? BasePoint = null);

public sealed record DraftingLimits(int MaximumPoints = 10_000, int MaximumEntities = 10_000,
    int MaximumClonedObjects = 100_000, double MaximumCoordinate = 1e12, double MinimumLength = 1e-9);

/// <summary>A complete, optimistic edit; preview by applying Patch to an immutable memory store.
/// Executing a plan creates exactly one canonical undo entry, irrespective of entity count.</summary>
public sealed record DraftingPlan(string Description, ShadowGraphPatch Patch, ImmutableArray<ObjectId> Created)
{
    public ShadowSnapshot Execute(EditorSession editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        return editor.Execute(Description, Patch);
    }
}

public sealed record DraftingGrip(string Id, ObjectId Entity, string Property, Vec3 Position, string Kind,
    int Index = -1);

public static class DraftingGuard
{
    public static void Current(ShadowSnapshot snapshot, RenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(scene);
        if (snapshot.DocumentId != scene.DocumentId || snapshot.Revision != scene.Revision)
            throw new ShadowConflictException("The drawing changed; obtain a current scene before editing.");
    }

    public static ShadowReader Entity(ShadowSnapshot snapshot, ObjectId id, bool writable = true)
    {
        var entity = snapshot.Read(id);
        if (entity.Object is not { } node || (node.Kind & ShadowObjectKind.Entity) == 0 && node.Type != "AttributeDefinition")
            throw new ArgumentException($"{id} is not a drawing entity.");
        if (node.Type == "AttributeDefinition" && (entity["Owner"].Id is not { } owner ||
            !DraftingAttributeDefinitions.Owner(snapshot, owner)["AttributeDefinitions"].Items.Any(entry => entry["Value"].Id == id)))
            throw new ArgumentException("The attribute definition is not owned by an editable drawing space.");
        if (writable && entity["Layer"]["IsLocked"].Boolean())
            throw new InvalidOperationException($"Layer '{entity["Layer"]["Name"].Text}' is locked.");
        return entity;
    }

    /// <summary>Root INSERTs are edited by default. Source identities are not browser row indices.</summary>
    public static ImmutableArray<ObjectId> Selection(ShadowSnapshot snapshot, RenderScene scene,
        IEnumerable<string> keys, bool writable = true)
    {
        Current(snapshot, scene);
        var pending = keys.ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<ObjectId>();
        foreach (var item in scene.Items)
            if (pending.Remove(item.Key)) ids.Add(item.InsertPath.IsEmpty ? item.Entity : item.InsertPath[0]);
        if (pending.Count != 0) throw new ShadowConflictException("Selection contains unknown or obsolete occurrences.");
        foreach (var id in ids) Entity(snapshot, id, writable);
        return ids.OrderBy(id => id.Value, StringComparer.Ordinal).ToImmutableArray();
    }

    public static void Planar(ShadowReader entity, double tolerance = 1e-9)
    {
        var normal = entity["Normal"].Vector(Vec3.UnitZ);
        if (Vec3.Distance(normal, Vec3.UnitZ) > tolerance || Math.Abs(entity["Elevation"].Number()) > tolerance)
            throw new NotSupportedException("This planar drafting command requires the world XY plane; source data was not changed.");
    }
}
