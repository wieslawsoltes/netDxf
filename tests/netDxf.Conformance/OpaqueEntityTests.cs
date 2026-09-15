using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private const string OpaqueName = "QUALIFIED_FUTURE_CURVE";
    private static void RegisterOpaqueEntityTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool input in new[] { false, true })
        {
            foreach (bool output in new[] { false, true })
                Run($"opaque-entity/packet/{version}/{input}/{output}", () => OpaquePacket(version, input, output));
            Run($"opaque-entity/lifecycle/{version}/{input}", () => OpaqueLifecycle(version, input));
            Run($"opaque-entity/common-edits/{version}/{input}", () => OpaqueCommonEdits(version, input));
        }
        foreach (bool binary in new[] { false, true })
            foreach (string defect in new[] { "missing-owner", "missing-handle", "duplicate-handle", "duplicate-owner", "missing-layer", "missing-target", "missing-appid", "duplicate-layer", "duplicate-common", "children", "embedded", "proxy-name", "proxy-subclass", "vertex-name", "vertex-subclass", "attribute-subclass", "block-subclass", "dimension-subclass", "modeler-subclass", "surface-subclass", "bad-scale", "bad-xdata", "class-field", "class-duplicate", "class-object" })
                Run($"opaque-entity/reject/{defect}/{binary}", () => OpaqueRejected(defect, binary));
    }

    // Declared schema fixture. The source scaffold is authored by netDxf; the unknown packet is
    // an explicit test schema, not a native CAD or application-private producer sample.
    private static DxfRawDocument OpaqueFixture(DxfVersion version, string defect = "")
    {
        var document = new DxfDocument(version);
        var line = new Line(Vector3.Zero, Vector3.UnitX);
        document.Entities.Add(line);
        document.ApplicationRegistries.Add(new ApplicationRegistry("OPAQUE_TEST"));
        document.Classes.Add(new DxfClass(OpaqueName, "AcDbQualifiedFutureCurve", "declared-schema") { IsEntity = defect != "class-object", InstanceCount = version == DxfVersion.AutoCad2000 ? null : 1 });
        using var original = new MemoryStream(); Check(document.Save(original), "Opaque scaffold save"); original.Position = 0;
        var raw = DxfRawDocument.Create(DxfRawDocument.Load(original).Tags.Where(tag => tag.Code != 999));
        var tags = new List<DxfTag>
        {
            new(0, defect == "proxy-name" ? "ACAD_PROXY_ENTITY" : defect == "vertex-name" ? "VERTEX" : OpaqueName),
            new(5, "F001"), new(330, line.Owner.Record.Handle),
            new(102, "{UNINTERPRETED"), new(5, "F123"), new(330, "DEAD"), new(66, (short)1), new(101, "Embedded Object"), new(102, "}"),
            new(100, "AcDbEntity"), new(8, "0"), new(6, "BYLAYER"), new(62, (short)3), new(48, 1.25),
            new(300, "unknown common field"),
            new(100, "AcDbQualifiedFutureCurve"), new(10, 9.5), new(20, -4.5), new(30, 2.0),
            new(5, "BEEF"), new(340, line.Handle), new(320, "DEAD"), new(329, "BEEF"),
            new(102, "{PRIVATE"), new(360, "DEAD"), new(102, "{NESTED"), new(100, "AcDbVertex"), new(102, "}"), new(102, "}"),
            new(310, new byte[] { 0, 255, 37, 10, 13 }), new(300, "private \\U+0041 text"),
            new(62, (short)191), new(48, -8.0),
            new(1001, "OPAQUE_TEST"), new(1000, "source xdata"), new(1005, line.Handle), new(1070, (short)4)
        };
        int first(short code) => tags.FindIndex(tag => tag.Code == code);
        int body = tags.FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbQualifiedFutureCurve");
        if (defect == "missing-owner") tags.RemoveAt(first(330));
        if (defect == "missing-handle") tags.RemoveAt(first(5));
        if (defect == "duplicate-owner") tags.Insert(3, new DxfTag(330, line.Owner.Record.Handle));
        if (defect == "duplicate-handle") tags.Insert(2, new DxfTag(5, "F001"));
        if (defect == "missing-layer") tags[first(8)] = new DxfTag(8, "ABSENT_LAYER");
        if (defect == "missing-target") tags[first(340)] = new DxfTag(340, "D00D");
        if (defect == "missing-appid") tags[first(1001)] = new DxfTag(1001, "ABSENT_APPID");
        if (defect == "duplicate-layer") tags.Insert(body, new DxfTag(8, "0"));
        if (defect == "duplicate-common") tags.Insert(body, new DxfTag(100, "AcDbEntity"));
        if (defect == "children") tags.Insert(body, new DxfTag(66, (short)1));
        if (defect == "embedded") tags.Insert(body, new DxfTag(101, "Embedded Object"));
        var subclasses = new Dictionary<string, string> { ["proxy-subclass"] = "AcDbProxyEntity", ["vertex-subclass"] = "AcDbVertex", ["attribute-subclass"] = "AcDbAttribute", ["block-subclass"] = "AcDbBlockReference", ["dimension-subclass"] = "AcDbDimension", ["modeler-subclass"] = "AcDbModelerGeometry", ["surface-subclass"] = "AcDbSurface" };
        if (subclasses.TryGetValue(defect, out string? subclass)) tags[body] = new DxfTag(100, subclass);
        if (defect == "bad-scale") tags[first(48)] = new DxfTag(48, 0.0);
        if (defect == "bad-xdata") tags.Add(new DxfTag(1002, "{"));
        var all = raw.Tags.ToList();
        var section = raw.Sections.Single(section => section.Name == "ENTITIES");
        int at = section.Records.Last().EndTagIndex;
        all.InsertRange(at, tags);
        if (defect is "class-field" or "class-duplicate")
        {
            var modified = DxfRawDocument.Create(all);
            var declaration = modified.Sections.Single(section => section.Name == "CLASSES").Records.Single(record => record.Tags.Any(tag => tag.Code == 1 && (string)tag.Value == OpaqueName));
            all.Insert(declaration.EndTagIndex, defect == "class-field" ? new DxfTag(301, "discarded field") : new DxfTag(90, 0));
        }
        return DxfRawDocument.Create(all);
    }
    private static DxfDocument OpaqueLoad(DxfRawDocument raw, bool binary)
    {
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        return DxfDocument.Load(input) ?? throw new Exception("Declared opaque fixture failed to load");
    }
    private static void OpaqueSameTags(IReadOnlyList<DxfTag> expected, IReadOnlyList<DxfTag> actual, string context)
    {
        Equal(expected.Count, actual.Count, context + " count");
        for (int i = 0; i < expected.Count; i++)
        {
            Equal(expected[i].Code, actual[i].Code, context + " code " + i);
            if (expected[i].Value is byte[] bytes) Check(bytes.SequenceEqual((byte[])actual[i].Value), context + " bytes " + i);
            else Equal(expected[i].Value, actual[i].Value, context + " value " + i);
        }
    }
    private static void OpaquePacket(DxfVersion version, bool input, bool output)
    {
        var source = OpaqueFixture(version);
        using (var sourceStream = new MemoryStream()) { source.Save(sourceStream, input); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"opaque-entity-source-{version}-{input}.dxf"), sourceStream.ToArray()); }
        var document = OpaqueLoad(source, input);
        var entity = document.Entities.OpaqueEntities.Single();
        var original = source.Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == OpaqueName);
        OpaqueSameTags(original.Tags, entity.SourceTags, "Original source snapshot");
        Check(ReferenceEquals(entity, document.GetObjectByHandle("F001")), "Actual registered unknown entity identity");
        Check(entity.References.Contains(document.Entities.Lines.Single()), "Exact known source pointer");
        Equal(Vector3.UnitZ, entity.Normal, "Normal compatibility placeholder");
        using var stream = new MemoryStream(); Check(document.Save(stream, output), "Unknown packet save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"opaque-entity-packet-{version}-{input}-{output}.dxf"), stream.ToArray()); stream.Position = 0;
        var written = DxfRawDocument.Load(stream).Sections.Single(section => section.Name == "ENTITIES").Records.Single(record => record.Name == OpaqueName);
        OpaqueSameTags(original.Tags, written.Tags, "Unchanged complete output packet");
        stream.Position = 0; var reloaded = DxfDocument.Load(stream) ?? throw new Exception("Opaque packet reload");
        OpaqueSameTags(entity.SourceTags, reloaded.Entities.OpaqueEntities.Single().SourceTags, "Second typed load");
    }
    private static void OpaqueLifecycle(DxfVersion version, bool binary)
    {
        var document = OpaqueLoad(OpaqueFixture(version), binary); var entity = document.Entities.OpaqueEntities.Single();
        var state = new CompatibilityState(document);
        foreach (Action action in new Action[] { () => entity.Clone(), () => entity.TransformBy(Matrix3.Identity, Vector3.UnitX), () => entity.TransformBy(new Matrix4(2,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1)), () => entity.Normal = Vector3.UnitX, () => entity.Owner.Clone("opaque-copy") })
        {
            bool rejected = false; try { action(); } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "Unknown geometry or cloning must reject"); state.CheckUnchanged();
        }
        entity.TransformBy(Matrix3.Identity, Vector3.Zero); entity.TransformBy(Matrix4.Identity); state.CheckUnchanged();
        Check(!document.Entities.Remove(document.Entities.Lines.Single()), "Opaque pointer prevents dependency removal");
        var target = SupportedVersions.First(value => value != version);
        var report = CompatibilityAnalyze(document, target);
        Check(report.Diagnostics.Any(item => item.Code == "STORED_SOURCE_PROFILE" && ReferenceEquals(item.SourceObject, entity)), "Opaque target profile diagnostic");
        document.DrawingVariables.AcadVer = target;
        var changed = new CompatibilityState(document); using var output = new MemoryStream();
        bool saved; try { saved = document.Save(output, binary); } catch (NotSupportedException) { saved = false; }
        Check(!saved && output.Length == 0, "Profile rejection before output"); changed.CheckUnchanged();
        document.DrawingVariables.AcadVer = version;
        Check(document.Entities.Remove(entity), "Explicit unreferenced unknown entity removal");
        Equal("F001", entity.SourceHandle, "Retired source identity snapshot");
        bool attachRejected = false; try { document.Entities.Add(entity); } catch (InvalidOperationException) { attachRejected = true; }
        Check(attachRejected, "Retired unknown entity cannot be reattached");
        Check(!document.AnalyzeVersionCompatibility(target).Diagnostics.Any(item => ReferenceEquals(item.SourceObject, entity)), "Fresh report omits retired unknown entity");
    }
    private static void OpaqueCommonEdits(DxfVersion version, bool binary)
    {
        var document = OpaqueLoad(OpaqueFixture(version), binary); var entity = document.Entities.OpaqueEntities.Single();
        var snapshot = entity.SourceTags.ToArray();
        entity.Color = AciColor.Red; entity.LinetypeScale = 4.0; entity.IsVisible = false;
        entity.Layer = new Layer("OPAQUE_EDIT"); entity.Lineweight = Lineweight.W50;
        entity.XData["OPAQUE_TEST"].XDataRecord.Add(new XDataRecord(XDataCode.String, "edited"));
        using var stream = new MemoryStream(); Check(document.Save(stream, !binary), "Common appearance edit save"); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new Exception("Common appearance edit reload");
        var other = loaded.Entities.OpaqueEntities.Single();
        Equal((short)1, other.Color.Index, "Edited ACI"); Equal(4.0, other.LinetypeScale, "Edited scale"); Check(!other.IsVisible, "Edited visibility");
        Equal("OPAQUE_EDIT", other.Layer.Name, "Edited registered layer"); Equal(Lineweight.W50, other.Lineweight, "Edited lineweight");
        OpaqueSameTags(snapshot, entity.SourceTags, "Edit preserves original snapshot");
        int body = Array.FindIndex(snapshot, tag => tag.Code == 100 && (string)tag.Value == "AcDbQualifiedFutureCurve");
        int otherBody = other.SourceTags.ToList().FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbQualifiedFutureCurve");
        OpaqueSameTags(snapshot.Skip(body).TakeWhile(tag => tag.Code != 1001).ToArray(), other.SourceTags.Skip(otherBody).TakeWhile(tag => tag.Code != 1001).ToArray(), "Common edit preserves private body");
    }
    private static void OpaqueRejected(string defect, bool binary)
    {
        var raw = OpaqueFixture(DxfVersion.AutoCad2018, defect);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(input) == null; }
        catch (Exception) { rejected = true; }
        Check(rejected, "Unsupported opaque envelope must reject: " + defect);
    }
}
