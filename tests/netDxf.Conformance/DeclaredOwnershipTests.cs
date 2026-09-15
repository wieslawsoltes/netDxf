using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterDeclaredOwnershipTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"declared-ownership/graph/{version}/{binary}", () => DeclaredOwnershipRoundTrip(version, binary));
        foreach (bool binary in new[] { false, true })
            Run($"declared-ownership/composite-roundtrip/{binary}", () => DeclaredOwnershipCompositeRoundtrip(binary));
        foreach (bool binary in new[] { false, true })
            foreach (bool duplicate in new[] { false, true })
                Run($"declared-ownership/malformed-envelope/{binary}/{duplicate}", () => DeclaredOwnershipMalformed(binary, duplicate));
        Run("declared-ownership/payload-protection", DeclaredOwnershipPayload);
        Run("declared-ownership/bind-atomicity", DeclaredOwnershipBindAtomicity);
        Run("declared-ownership/adoption-atomicity", DeclaredOwnershipAdoptionAtomicity);
        Run("declared-ownership/reference-copy", DeclaredOwnershipReferenceCopy);
        Run("declared-ownership/opaque-clone-boundary", DeclaredOwnershipOpaqueClone);
        Run("declared-ownership/registered-reciprocity", DeclaredOwnershipRegisteredReciprocity);
    }

    // These opaque child bodies are structural test carriers, not an editable TABLE implementation.
    // The ownership marker and 360/361 relationships come from the frozen native TABLE fixtures.
    private static DxfDatabaseObject OwnershipChild(string kind)
    {
        var tags = new List<DxfTag> { new(100, kind == "TABLECONTENT" ? "AcDbTableContent" : "AcDbTableGeometry"), new(90, 0) };
        return (DxfDatabaseObject)Activator.CreateInstance(typeof(DxfOpaqueObject), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { kind, tags }, null)!;
    }

    private static DxfXRecord OwnershipRecord()
    {
        var record = new DxfXRecord();
        foreach (DxfTag tag in new DxfTag[] { new(102, "ACAD_ROUNDTRIP_2008_TABLE_ENTITY"), new(360, "0"), new(70, (short)2), new(90, 1),
            new(10, 0.0), new(20, 0.0), new(30, 0.0), new(90, 0), new(90, 2), new(361, "0") }) record.Data.Add(tag);
        return record;
    }

    private static (DxfXRecord Record, DxfDatabaseObject Content, DxfDatabaseObject Geometry) OwnershipGraph()
    {
        var record = OwnershipRecord(); var content = OwnershipChild("TABLECONTENT"); var geometry = OwnershipChild("TABLEGEOMETRY");
        OwnershipInvoke(record, "BindTableRoundtripChildren", content, geometry);
        return (record, content, geometry);
    }

    private static object? OwnershipInvoke(object target, string method, params object[] arguments)
    {
        try { return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static void OwnershipSetOwner(DxfObject target, DxfObject? owner)
    { typeof(DxfObject).GetProperty(nameof(DxfObject.Owner))!.SetValue(target, owner); }

    private static long OwnershipSeed(DxfDocument doc)
    { return (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!; }

    private static void DeclaredOwnershipRoundTrip(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var graph = OwnershipGraph();
        var dictionary = new DxfDictionary(); dictionary.Add("ACAD_XREC_ROUNDTRIP", graph.Record);
        doc.Objects.Root.Add("OWNERSHIP_TEST", dictionary);
        foreach (DxfDatabaseObject child in new[] { graph.Content, graph.Geometry })
        {
            Check(ReferenceEquals(graph.Record, child.Owner), "schema child owner");
            Check(ReferenceEquals(doc.Objects, child.Database) && ReferenceEquals(child, doc.GetObjectByHandle(child.Handle)), "schema child registration");
        }
        Equal(graph.Content.Handle, (string)graph.Record.Data.Single(t => t.Code == 360).Value, "materialized content handle");
        Equal(graph.Geometry.Handle, (string)graph.Record.Data.Single(t => t.Code == 361).Value, "materialized geometry handle");
        Equal(0, doc.Objects.Validate().Count, "declared graph validation");
        Throws<ArgumentException>(() => dictionary.Add("WRONG_OWNER", graph.Content));
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "declared graph save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"declared-ownership-{version}-{(binary ? "binary" : "text")}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new Exception("Declared graph reload failed.");
        var record = (DxfXRecord)loaded.GetObjectByHandle(graph.Record.Handle);
        Check(record.IsSchemaManaged, "ownership binding lost on input");
        Equal(0, loaded.Objects.Validate().Count, "loaded declared graph validation");
        foreach (DxfTag tag in record.Data.Where(t => t.Code is 360 or 361))
        {
            var child = (DxfDatabaseObject)loaded.GetObjectByHandle((string)tag.Value);
            Check(ReferenceEquals(record, child.Owner), "loaded reciprocal ownership");
            Equal(tag.Code == 360 ? "TABLECONTENT" : "TABLEGEOMETRY", child.CodeName, "loaded child kind");
        }
    }

    private static void DeclaredOwnershipCompositeRoundtrip(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2004); var graph = OwnershipGraph(); doc.Objects.Root.Add("GRAPH", graph.Record);
        using var saved = new MemoryStream(); Check(doc.Save(saved, binary), "composite setup save"); saved.Position = 0;
        var raw = DxfRawDocument.Load(saved);
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "XRECORD");
        // The native R2004 corpus carries these additional sections and another 360 slot.
        // Reuse a structural carrier target here; actual native DATATABLE packets are pinned separately.
        var tags = record.Tags.Concat(new DxfTag[] { new(102, "ACAD_ROUNDTRIP_PRE2007_TABLE"), new(90, 7), new(91, 3),
            new(102, "ACAD_ROUNDTRIP_PRE2007_TABLECELL"), new(360, graph.Content.Handle) }).ToArray();
        raw = raw.WithRecord(record, tags);
        using var input = new MemoryStream(); raw.Save(input); input.Position = 0;
        var loaded = DxfDocument.Load(input) ?? throw new Exception("Composite envelope load failed.");
        var result = (DxfXRecord)loaded.GetObjectByHandle(graph.Record.Handle);
        Check(!result.IsSchemaManaged, "An unqualified composite envelope became managed");
        Equal(15, result.Data.Count, "Composite envelope payload length");
        Equal("ACAD_ROUNDTRIP_PRE2007_TABLECELL", (string)result.Data[13].Value, "Composite section marker");
        using var output = new MemoryStream(); Check(loaded.Save(output, binary), "Composite envelope save failed"); output.Position = 0;
        var second = DxfDocument.Load(output) ?? throw new Exception("Composite envelope second load failed.");
        var twice = (DxfXRecord)second.GetObjectByHandle(result.Handle);
        Check(result.Data.Select(t => (t.Code, t.Value)).SequenceEqual(twice.Data.Select(t => (t.Code, t.Value))), "Composite envelope changed on roundtrip");
    }

    private static void DeclaredOwnershipMalformed(bool binary, bool duplicate)
    {
        var doc = new DxfDocument(); var graph = OwnershipGraph(); doc.Objects.Root.Add("GRAPH", graph.Record);
        using var saved = new MemoryStream(); Check(doc.Save(saved, binary), "malformed setup save"); saved.Position = 0;
        var raw = DxfRawDocument.Load(saved); var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "XRECORD");
        var tags = duplicate ? record.Tags.Concat(new[] { new DxfTag(360, graph.Content.Handle) }) : record.Tags.Where(t => t.Code != 361);
        using var input = new MemoryStream(); raw.WithRecord(record, tags).Save(input); input.Position = 0;
        bool rejected;
        try { rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "Malformed single-section envelope loaded");
    }

    private static void DeclaredOwnershipPayload()
    {
        var doc = new DxfDocument(); var graph = OwnershipGraph(); doc.Objects.Root.Add("GRAPH", graph.Record);
        var before = graph.Record.Data.ToArray(); long seed = OwnershipSeed(doc);
        foreach (Action mutation in new Action[] {
            () => graph.Record.Data.Add(new DxfTag(340, "7FFFFFFFFFFFFFFF")),
            () => graph.Record.Data.Insert(0, new DxfTag(1, "before")),
            () => graph.Record.Data[0] = new DxfTag(102, "changed"),
            () => graph.Record.Data.RemoveAt(1), () => graph.Record.Data.Clear() })
            Throws<InvalidOperationException>(mutation);
        Check(before.SequenceEqual(graph.Record.Data), "rejected edit changed managed payload");
        Equal(seed, OwnershipSeed(doc), "rejected edit reserved handle");
        var ordinary = new DxfXRecord(); Check(!ordinary.IsSchemaManaged, "ordinary record became managed");
        ordinary.Data.Add(new DxfTag(1, "one")); ordinary.Data[0] = new DxfTag(1, "two"); ordinary.Data.RemoveAt(0); ordinary.Data.Clear();
    }

    private static void DeclaredOwnershipBindAtomicity()
    {
        foreach (int scenario in Enumerable.Range(0, 7))
        {
            var record = OwnershipRecord(); var content = OwnershipChild("TABLECONTENT"); var geometry = OwnershipChild("TABLEGEOMETRY");
            DxfObject? oldOwner = null;
            switch (scenario)
            {
                case 0: record.Data[0] = new DxfTag(102, "UNKNOWN_SCHEMA"); break;
                case 1: record.Data.RemoveAt(record.Data.Count - 1); break;
                case 2: record.Data.Add(new DxfTag(360, "0")); break;
                case 3: record.Data.Add(new DxfTag(350, "0")); break;
                case 4: oldOwner = new DxfDictionary(); OwnershipSetOwner(geometry, oldOwner); break;
                case 5: OwnershipSetOwner(record, geometry); break;
                case 6: geometry = OwnershipChild("WRONG_KIND"); break;
            }
            var tags = record.Data.ToArray();
            Throws<ArgumentException>(() => OwnershipInvoke(record, "BindTableRoundtripChildren", content, geometry));
            Check(!record.IsSchemaManaged && content.Owner == null && ReferenceEquals(oldOwner, geometry.Owner), "failed binding changed child owners");
            Check(tags.SequenceEqual(record.Data) && record.Handle == null && content.Handle == null && geometry.Handle == null, "failed binding changed identities or data");
        }
        var foreign = new DxfDocument(); var registered = OwnershipChild("TABLECONTENT"); foreign.Objects.Root.Add("FOREIGN", registered);
        string handle = registered.Handle; var detached = OwnershipRecord(); var other = OwnershipChild("TABLEGEOMETRY");
        Throws<ArgumentException>(() => OwnershipInvoke(detached, "BindTableRoundtripChildren", registered, other));
        Check(registered.Handle == handle && registered.Database == foreign.Objects && foreign.GetObjectByHandle(handle) == registered && other.Owner == null, "foreign binding transferred identity");
    }

    private static void DeclaredOwnershipAdoptionAtomicity()
    {
        var doc = new DxfDocument(); var graph = OwnershipGraph(); var outside = new DxfPlaceholder();
        graph.Content.PersistentReactors.Add(outside);
        int count = doc.Objects.Items.Count; long seed = OwnershipSeed(doc);
        Throws<ArgumentException>(() => doc.Objects.Root.Add("GRAPH", graph.Record));
        Equal(seed, OwnershipSeed(doc), "failed adoption changed allocation seed"); Equal(count, doc.Objects.Items.Count, "failed adoption registered partial graph");
        Check(!doc.Objects.Root.Contains("GRAPH") && graph.Record.Owner == null && graph.Record.Handle == null && graph.Content.Handle == null && graph.Geometry.Handle == null, "failed adoption changed graph");
        graph.Content.PersistentReactors.Clear();
        OwnershipSetOwner(graph.Geometry, new DxfDictionary());
        Throws<ArgumentException>(() => doc.Objects.Root.Add("GRAPH", graph.Record));
        Equal(seed, OwnershipSeed(doc), "reciprocity failure allocated handles");
        OwnershipSetOwner(graph.Geometry, graph.Record); doc.Objects.Root.Add("GRAPH", graph.Record);
        Equal(0, doc.Objects.Validate().Count, "repaired adoption failed");
    }

    private static void DeclaredOwnershipReferenceCopy()
    {
        var graph = OwnershipGraph(); var source = new DxfDocument(); source.Objects.Root.Add("SOURCE", graph.Record);
        var copiedRecord = (DxfXRecord)OwnershipInvoke(graph.Record, "CloneShell")!;
        var copiedContent = OwnershipChild("TABLECONTENT"); var copiedGeometry = OwnershipChild("TABLEGEOMETRY");
        Func<DxfObject, DxfObject> resolve = item => ReferenceEquals(item, graph.Content) ? copiedContent : ReferenceEquals(item, graph.Geometry) ? copiedGeometry : throw new Exception("Unexpected reference");
        OwnershipInvoke(graph.Record, "CopyDatabaseReferencesTo", copiedRecord, resolve);
        var destination = new DxfDocument(); destination.Objects.Root.Add("COPIED", copiedRecord);
        Check(ReferenceEquals(copiedContent.Owner, copiedRecord) && ReferenceEquals(copiedGeometry.Owner, copiedRecord), "reference copy retained source owners");
        Check(ReferenceEquals(graph.Content.Owner, graph.Record) && ReferenceEquals(graph.Geometry.Owner, graph.Record), "reference copy changed source owners");
        Equal(copiedContent.Handle, (string)copiedRecord.Data.Single(t => t.Code == 360).Value, "copied schema handle");
        Equal(0, destination.Objects.Validate().Count, "copied ownership validation");
    }

    private static void DeclaredOwnershipOpaqueClone()
    {
        var doc = new DxfDocument(); var graph = OwnershipGraph(); doc.Objects.Root.Add("SOURCE", graph.Record);
        int count = doc.Objects.Items.Count; long seed = OwnershipSeed(doc);
        Throws<NotSupportedException>(() => doc.Objects.CloneObject(graph.Record, doc.Objects.Root, "OPAQUE_COPY"));
        Equal(seed, OwnershipSeed(doc), "opaque clone allocated handles"); Equal(count, doc.Objects.Items.Count, "opaque clone registered partial graph");
        Check(!doc.Objects.Root.Contains("OPAQUE_COPY"), "opaque clone added destination name");
    }

    private static void DeclaredOwnershipRegisteredReciprocity()
    {
        var doc = new DxfDocument(); var graph = OwnershipGraph(); doc.Objects.Root.Add("SOURCE", graph.Record);
        OwnershipSetOwner(graph.Geometry, doc.Objects.Root);
        Check(doc.Objects.Validate().Any(e => e.Contains("not reciprocal")), "registered ownership corruption was missed");
        using var output = new MemoryStream(new byte[16], true); long length = output.Length; long position = output.Position;
        Throws<InvalidOperationException>(() => doc.Save(output));
        Equal(length, output.Length, "invalid graph changed destination length"); Equal(position, output.Position, "invalid graph changed destination position");
    }
}
