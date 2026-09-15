using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;

namespace NetDxf.Conformance;
internal static partial class Program
{
    // Register after both the declared-ownership and terminal-erasure modules are integrated.
    private static void RegisterErasureDeclaredOwnershipTests()
    {
        Run("erasure-declared-ownership/erased-wrapper-bind", ErasureDeclaredWrapper);
        foreach (bool geometry in new[] { false, true })
        {
            Run("erasure-declared-ownership/erased-candidate/" + geometry, () => ErasureDeclaredCandidate(geometry));
            foreach (bool extension in new[] { false, true })
                Run($"erasure-declared-ownership/terminal-descendant/{geometry}/{extension}", () => ErasureDeclaredDescendant(geometry, extension));
        }
    }

    private static void ErasureDeclaredWrapper()
    {
        var doc = new DxfDocument(); var record = OwnershipRecord(); doc.NamedObjects.Add("WRAPPER", record);
        string handle = record.Handle; doc.Objects.EraseOwnedTree(record);
        var content = OwnershipChild("TABLECONTENT"); var geometry = OwnershipChild("TABLEGEOMETRY"); var tags = record.Data.ToArray();
        ErasureReject(doc, () => OwnershipInvoke(record, "BindTableRoundtripChildren", content, geometry));
        Check(record.IsErased && record.Database == null && record.Handle == handle && !record.IsSchemaManaged, "Erased wrapper was resurrected by binding.");
        Check(content.Owner == null && geometry.Owner == null && content.Handle == null && geometry.Handle == null && tags.SequenceEqual(record.Data), "Rejected terminal binding changed payload or child owners.");
    }

    private static void ErasureDeclaredCandidate(bool erasedGeometry)
    {
        var doc = new DxfDocument(); var dead = new DxfXRecord(); doc.NamedObjects.Add("ERASE", dead); doc.Objects.EraseOwnedTree(dead);
        // No public typed TABLECONTENT/TABLEGEOMETRY authoring exists yet. Retag an actual
        // tombstone only in this defensive test to reach the candidate terminal-state guard.
        typeof(DxfObject).GetField("codename", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dead, erasedGeometry ? "TABLEGEOMETRY" : "TABLECONTENT");
        var record = OwnershipRecord(); var content = erasedGeometry ? OwnershipChild("TABLECONTENT") : dead; var geometry = erasedGeometry ? dead : OwnershipChild("TABLEGEOMETRY");
        var tags = record.Data.ToArray(); string handle = dead.Handle;
        ErasureReject(doc, () => OwnershipInvoke(record, "BindTableRoundtripChildren", content, geometry));
        Check(!record.IsSchemaManaged && record.Owner == null && record.Handle == null && content.Owner == null && geometry.Owner == null, "Erased candidate binding partially assigned ownership.");
        Check(dead.IsErased && dead.Handle == handle && tags.SequenceEqual(record.Data), "Erased candidate binding changed identity or wrapper payload.");
    }

    private static void ErasureDeclaredDescendant(bool erasedGeometry, bool extension)
    {
        var doc = new DxfDocument(); var graph = OwnershipGraph(); var container = new DxfDictionary(); container.Add("WRAPPER", graph.Record);
        var dead = erasedGeometry ? graph.Geometry : graph.Content;
        // A consistent public graph cannot acquire this state: opaque erasure rejects and
        // incoming owner slots protect children. Deliberately corrupt the flag to prove the
        // generic adoption walk checks every declared descendant before allocating handles.
        typeof(DxfDatabaseObject).GetProperty(nameof(DxfDatabaseObject.IsErased))!.SetValue(dead, true);
        var host = new Line(); doc.Entities.Add(host); var before = graph.Record.Data.ToArray();
        ErasureReject(doc, () => { if (extension) doc.Objects.SetExtensionDictionary(host, container); else doc.NamedObjects.Add("GRAPH", container); });
        Check(container.Database == null && container.Owner == null && container.Handle == null && host.ExtensionDictionary == null, "Terminal descendant adopted the detached root.");
        Check(graph.Record.Database == null && graph.Record.Handle == null && graph.Content.Handle == null && graph.Geometry.Handle == null && before.SequenceEqual(graph.Record.Data), "Terminal descendant rejection allocated or materialized payload.");
        Check(ReferenceEquals(graph.Content.Owner, graph.Record) && ReferenceEquals(graph.Geometry.Owner, graph.Record), "Terminal descendant rejection changed input ownership.");
    }
}
