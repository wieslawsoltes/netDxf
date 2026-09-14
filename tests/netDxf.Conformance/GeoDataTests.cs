using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunGeoDataTests()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
            foreach (bool binary in new[] { false, true })
            {
                Run($"geodata/authored/{version}/{binary}", () => GeoDataRoundTrip(version, binary));
                Run($"geodata/independent/{version}/{binary}", () => GeoDataIndependent(version, binary));
            }
        Run("geodata/finite-and-transport-validation", GeoDataInvalidValues);
        Run("geodata/mesh-update-and-save-atomicity", GeoDataMeshAtomicity);
        Run("geodata/attachment-policy", GeoDataAttachmentPolicy);
        Run("geodata/extension-graph-clone", GeoDataClone);
        foreach (int unsupportedVersion in new[] { 1, 3, 99 })
            Run($"geodata/opaque-version/{unsupportedVersion}", () => GeoDataOpaque(unsupportedVersion, false));
        Run("geodata/opaque-private-payload", () => GeoDataOpaque(2, true));
        foreach (string defect in new[] { "point-count", "face-count", "negative-count", "face-index", "missing-coordinate", "missing-definition", "duplicate-scalar", "wrong-host", "late-chunk", "orphan-host" })
            Run("geodata/reject-malformed/" + defect, () => GeoDataMalformed(defect));
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004, DxfVersion.AutoCad2007 })
            Run("geodata/version-gate/" + version, () => GeoDataVersionGate(version));
    }
    private static BlockRecord GeoHost(DxfDocument doc) => doc.Blocks[Block.DefaultModelSpaceName].Record;
    private static DxfGeoData MakeGeoData(DxfDocument doc)
    {
        var data = new DxfGeoData(GeoHost(doc))
        {
            CoordinateType = DxfGeoCoordinateType.LocalGrid, DesignPoint = new Vector3(1.25, -2.5, 7.75), ReferencePoint = new Vector3(11.5, 48.25, 100.125),
            HorizontalUnitScale = 0.3048, VerticalUnitScale = 0.0254, HorizontalUnits = DrawingUnits.Feet, VerticalUnits = DrawingUnits.Inches,
            NorthDirection = new Vector2(3, 4), UpDirection = new Vector3(2, 3, 4), ScaleEstimation = DxfGeoScaleEstimation.UserScale,
            UserScaleFactor = 0.9996, SeaLevelCorrection = true, SeaLevelElevation = -17.25, CoordinateProjectionRadius = 6378137,
            CoordinateSystemDefinition = new string('x', 252) + "🧪 Żółć\\U+0041\n" + new string('z', 600),
            GeoRssTag = "metadata", ObservationFrom = "origin Żółć", ObservationTo = "target 東京", ObservationCoverage = "survey bounds"
        };
        data.SetMesh(new[] { new DxfGeoMeshPoint(new Vector2(0, 0), new Vector2(11, 48)), new DxfGeoMeshPoint(new Vector2(1, 0), new Vector2(12, 48)), new DxfGeoMeshPoint(new Vector2(0, 1), new Vector2(11, 49)) }, new[] { new DxfGeoMeshFace(0, 1, 2) });
        doc.Objects.SetGeoData(data); return data;
    }
    private static void GeoDataRoundTrip(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); DxfGeoData original = MakeGeoData(doc);
        string definition = original.CoordinateSystemDefinition, handle = original.Handle;
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "GEODATA save");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"geodata-{version}-{(binary ? "binary" : "ascii")}.dxf"), stream.ToArray());
            stream.Position = 0; var raw = DxfRawDocument.Load(stream);
            var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "GEODATA");
            var chunks = record.Tags.Where(t => t.Code == 301 || t.Code == 303).ToArray();
            Check(chunks.Length > 2 && chunks.Last().Code == 301 && chunks.Take(chunks.Length - 1).All(t => t.Code == 303), "definition chunk order");
            Check(chunks.All(t => ((string)t.Value).Length <= 255), "definition chunk limit");
            var cls = raw.Sections.Single(s => s.Name == "CLASSES").Records.Single(r => r.Tags.Any(t => t.Code == 1 && Equals(t.Value, "GEODATA")));
            Check(cls.Tags.Any(t => t.Code == 2 && Equals(t.Value, "AcDbGeoData")) && cls.Tags.Any(t => t.Code == 91 && Equals(t.Value, 1)), "required GEODATA class");
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new Exception("GEODATA load failed.");
            DxfGeoData data = doc.Objects.GetGeoData(GeoHost(doc)) ?? throw new Exception("Missing GEODATA.");
            Equal(handle, data.Handle, "stable GEODATA identity");
            Equal(definition, data.CoordinateSystemDefinition, "full chunked definition including literal escapes");
            Equal(new Vector2(3, 4), data.NorthDirection, "nonunit north"); Equal(new Vector3(2, 3, 4), data.UpDirection, "nonunit up");
            Equal(new Vector3(1.25, -2.5, 7.75), data.DesignPoint, "design point"); Equal(new Vector3(11.5, 48.25, 100.125), data.ReferencePoint, "reference point");
            Equal(0.3048, data.HorizontalUnitScale, "horizontal scale"); Equal(0.0254, data.VerticalUnitScale, "vertical scale"); Equal(0.9996, data.UserScaleFactor, "user scale");
            Equal(DrawingUnits.Feet, data.HorizontalUnits, "horizontal units"); Equal(DrawingUnits.Inches, data.VerticalUnits, "vertical units");
            Check(data.SeaLevelCorrection, "sea level flag"); Equal(-17.25, data.SeaLevelElevation, "sea level elevation"); Equal(6378137.0, data.CoordinateProjectionRadius, "radius");
            Equal(3, data.MeshPoints.Count, "point count"); Equal(1, data.MeshFaces.Count, "face count"); Equal(2, data.MeshFaces[0].Third, "mesh index");
            Equal("target 東京", data.ObservationTo, "Unicode metadata"); Equal(0, doc.Objects.Validate().Count, "loaded graph");
            Check(ReferenceEquals(data.Owner, GeoHost(doc).ExtensionDictionary), "extension ownership");
        }
    }
    private static void GeoDataIndependent(DxfVersion version, bool binary)
    {
        string year = version.ToString().Substring("AutoCad".Length), directory = Path.Combine("tests", "fixtures", "geodata");
        string input = Path.Combine(directory, $"independent-geodata-R{year}.dxf");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var expected = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("file").GetString() == Path.GetFileName(input));
        var doc = DxfDocument.Load(input) ?? throw new Exception("Independent GEODATA load failed.");
        var data = doc.Objects.GetGeoData(GeoHost(doc)) ?? throw new Exception("Independent GEODATA was not typed.");
        Equal(expected.GetProperty("geodata").GetString()!, data.Handle, "source identity");
        Equal(expected.GetProperty("coordinate_system_definition").GetString()!, data.CoordinateSystemDefinition, "source full XML");
        var attrs = expected.GetProperty("attributes");
        Equal(attrs.GetProperty("design_point")[0].GetDouble(), data.DesignPoint.X, "source precision");
        Equal((DxfGeoScaleEstimation)attrs.GetProperty("scale_estimation_method").GetInt32(), data.ScaleEstimation, "source scale method");
        Equal(new Vector2(3, 4), data.NorthDirection, "source north"); Equal(new Vector3(2, 3, 4), data.UpDirection, "source up");
        Equal(5, data.MeshPoints.Count, "source point count"); Equal(4, data.MeshFaces.Count, "source face count");
        for (int i = 0; i < data.MeshPoints.Count; i++)
        {
            var source = expected.GetProperty("source_vertices")[i]; var target = expected.GetProperty("target_vertices")[i];
            Equal(new Vector2(source[0].GetDouble(), source[1].GetDouble()), data.MeshPoints[i].Source, "source mesh coordinate");
            Equal(new Vector2(target[0].GetDouble(), target[1].GetDouble()), data.MeshPoints[i].Target, "target mesh coordinate");
        }
        Equal(0, doc.Objects.Validate().Count, "independent graph"); Check(data.XData.Count > 0 && data.PersistentReactors.Count > 0, "common data lost");
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "independent GEODATA save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"independent-geodata-R{year}-{(binary ? "binary" : "ascii")}.dxf"), stream.ToArray());
        stream.Position = 0; var edited = DxfDocument.Load(stream) ?? throw new Exception("Independent reload failed.");
        var edit = edited.Objects.GetGeoData(GeoHost(edited)); edit.SeaLevelElevation = 42.125; edit.MeshPoints[0] = new DxfGeoMeshPoint(new Vector2(9, 8), edit.MeshPoints[0].Target);
        using var second = new MemoryStream(); Check(edited.Save(second, !binary), "edited independent GEODATA save"); second.Position = 0;
        var loaded = DxfDocument.Load(second) ?? throw new Exception("Edited reload failed."); var result = loaded.Objects.GetGeoData(GeoHost(loaded));
        Equal(42.125, result.SeaLevelElevation, "edited elevation"); Equal(new Vector2(9, 8), result.MeshPoints[0].Source, "edited mesh");
        Equal(data.CoordinateSystemDefinition, result.CoordinateSystemDefinition, "unmodified XML");
    }
    private static void GeoDataInvalidValues()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var data = new DxfGeoData(GeoHost(doc));
        Throws<ArgumentOutOfRangeException>(() => data.HorizontalUnitScale = 0); Throws<ArgumentOutOfRangeException>(() => data.VerticalUnitScale = double.NaN);
        Throws<ArgumentOutOfRangeException>(() => data.UserScaleFactor = double.PositiveInfinity); Throws<ArgumentOutOfRangeException>(() => data.CoordinateProjectionRadius = -1);
        Throws<ArgumentOutOfRangeException>(() => data.DesignPoint = new Vector3(0, double.NaN, 1)); Throws<ArgumentOutOfRangeException>(() => data.UpDirection = Vector3.Zero);
        Throws<ArgumentOutOfRangeException>(() => data.NorthDirection = Vector2.Zero); Throws<ArgumentOutOfRangeException>(() => data.CoordinateType = (DxfGeoCoordinateType)4);
        Throws<ArgumentOutOfRangeException>(() => data.ScaleEstimation = (DxfGeoScaleEstimation)0); Throws<ArgumentOutOfRangeException>(() => data.HorizontalUnits = (DrawingUnits)25);
        Throws<ArgumentException>(() => data.CoordinateSystemDefinition = "literal^J"); Throws<ArgumentException>(() => data.CoordinateSystemDefinition = "CR\rLF\n");
        Throws<ArgumentException>(() => data.CoordinateSystemDefinition = "unpaired\uD800"); Throws<ArgumentException>(() => data.ObservationTo = "unpaired\uDC00");
        Throws<ArgumentException>(() => data.GeoRssTag = "line\nline"); Throws<ArgumentNullException>(() => data.MeshPoints.Add(null!));
        Throws<ArgumentOutOfRangeException>(() => new DxfGeoMeshPoint(new Vector2(double.NegativeInfinity, 0), Vector2.Zero));
        Throws<ArgumentOutOfRangeException>(() => new DxfGeoMeshFace(0, -1, 0));
    }
    private static void GeoDataMeshAtomicity()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var data = MakeGeoData(doc); var original = data.MeshPoints[0];
        Throws<ArgumentException>(() => data.SetMesh(new[] { original }, new[] { new DxfGeoMeshFace(0, 1, 2) })); Equal(3, data.MeshPoints.Count, "failed SetMesh retained points");
        IEnumerable<DxfGeoMeshPoint> Fail() { yield return original; throw new IOException("enumeration failed"); }
        Throws<IOException>(() => data.SetMesh(Fail(), Array.Empty<DxfGeoMeshFace>())); Equal(1, data.MeshFaces.Count, "failed enumeration retained faces");
        data.MeshPoints.RemoveAt(2); Check(doc.Objects.Validate().Count > 0, "dangling mesh index accepted");
        string seed = doc.DrawingVariables.HandleSeed; int layouts = doc.Layouts.Count;
        using var stream = new MemoryStream(); bool rejected;
        try { rejected = !doc.Save(stream); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && stream.Length == 0, "invalid mesh wrote output"); Equal(seed, doc.DrawingVariables.HandleSeed, "invalid save seed"); Equal(layouts, doc.Layouts.Count, "invalid save layouts");
        data.MeshFaces.Clear(); using var repaired = new MemoryStream(); Check(doc.Save(repaired), "repaired mesh save");
    }
    private static void GeoDataAttachmentPolicy()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var data = MakeGeoData(doc); int count = doc.Objects.Items.Count;
        var replacement = new DxfGeoData(GeoHost(doc)); Throws<InvalidOperationException>(() => doc.Objects.SetGeoData(replacement));
        Equal(count, doc.Objects.Items.Count, "duplicate attachment registration"); Check(replacement.Database == null && replacement.Owner == null, "duplicate adopted metadata");
        var other = new DxfDocument(DxfVersion.AutoCad2018); Throws<ArgumentException>(() => other.Objects.SetGeoData(new DxfGeoData(GeoHost(doc))));
        var block = other.Blocks.Add(new Block("Plain")); var ext = new DxfDictionary(); ext.Add("APP", new DxfXRecord()); other.Objects.SetExtensionDictionary(block.Record, ext);
        var additional = new DxfGeoData(block.Record); other.Objects.SetGeoData(additional); Check(ext.Contains("APP") && ext.Contains("ACAD_GEOGRAPHICDATA"), "existing extension entry lost");
        Equal(0, other.Objects.Validate().Count, "generic host attachment");
    }
    private static void GeoDataClone()
    {
        var source = new DxfDocument(DxfVersion.AutoCad2018); var original = MakeGeoData(source);
        var external = new DxfXRecord(); source.NamedObjects.Add("EXTERNAL", external); original.PersistentReactors.Add(external);
        var metadata = new XData(new netDxf.Tables.ApplicationRegistry("GEODATA_CLONE"));
        metadata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, external.Handle)); original.XData.Add(metadata);
        var target = new DxfDocument(DxfVersion.AutoCad2018);
        int initialCount = target.Objects.Items.Count; string initialSeed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.CloneExtensionDictionary(GeoHost(source), GeoHost(target)));
        Equal(initialCount, target.Objects.Items.Count, "missing mapping registration atomicity"); Equal(initialSeed, target.DrawingVariables.HandleSeed, "missing mapping seed atomicity");
        var mapped = new DxfXRecord(); target.NamedObjects.Add("EXTERNAL", mapped);
        target.Objects.CloneExtensionDictionary(GeoHost(source), GeoHost(target), new Dictionary<DxfObject, DxfObject> { [external] = mapped });
        var clone = target.Objects.GetGeoData(GeoHost(target)) ?? throw new Exception("Cloned GEODATA missing."); Check(!ReferenceEquals(clone, original), "clone identity");
        Check(ReferenceEquals(clone.HostBlock, GeoHost(target)), "clone host mapping"); Equal(0, target.Objects.Validate().Count, "cloned graph validity");
        Check(ReferenceEquals(clone.PersistentReactors.Single(), mapped), "mapped GEODATA reactor");
        Equal(mapped.Handle, (string)clone.XData["GEODATA_CLONE"].XDataRecord.Single().Value, "mapped GEODATA XData reference");
        clone.MeshPoints.RemoveAt(2); Equal(3, original.MeshPoints.Count, "clone mesh independence"); clone.MeshFaces.Clear();
        var second = source.Blocks.Add(new Block("GeographicCopy")); source.Objects.CloneExtensionDictionary(GeoHost(source), second.Record);
        Check(ReferenceEquals(source.Objects.GetGeoData(second.Record).HostBlock, second.Record), "same-document host remap");
        int count = source.Objects.Items.Count; string seed = source.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => source.Objects.Clone(GeoHost(source).ExtensionDictionary, source.NamedObjects, "INVALID_CLONE"));
        Equal(count, source.Objects.Items.Count, "rejected clone atomicity"); Equal(seed, source.DrawingVariables.HandleSeed, "rejected clone seed");
        var old = new DxfDocument(DxfVersion.AutoCad2007); var oldExternal = new DxfXRecord(); old.NamedObjects.Add("EXTERNAL", oldExternal); int oldCount = old.Objects.Items.Count;
        Throws<InvalidOperationException>(() => old.Objects.CloneExtensionDictionary(GeoHost(source), GeoHost(old), new Dictionary<DxfObject, DxfObject> { [external] = oldExternal })); Equal(oldCount, old.Objects.Items.Count, "downgrade clone atomicity");
    }
    private static DxfRawDocument GeoDataRaw(out string handle)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); handle = MakeGeoData(doc).Handle;
        using var stream = new MemoryStream(); Check(doc.Save(stream, true), "GEODATA source save"); stream.Position = 0; return DxfRawDocument.Load(stream);
    }
    private static void GeoDataOpaque(int version, bool privatePayload)
    {
        var raw = GeoDataRaw(out string handle);
        raw = ObjectStoreReplaceRecord(raw, handle, tags => { int i = tags.FindIndex(t => t.Code == 90); tags[i] = new DxfTag(90, version); if (privatePayload) tags.Add(new DxfTag(300, "private schema extension")); return tags; });
        using var stream = new MemoryStream(); raw.Save(stream); stream.Position = 0;
        var doc = DxfDocument.Load(stream) ?? throw new Exception("Opaque GEODATA load failed.");
        Check(doc.GetObjectByHandle(handle) is DxfOpaqueObject && doc.Objects.GetGeoData(GeoHost(doc)) == null, "unsupported GEODATA was typed");
        var opaque = (DxfOpaqueObject)doc.GetObjectByHandle(handle); var tagsBefore = opaque.Tags.ToArray();
        using var saved = new MemoryStream(); Check(doc.Save(saved, true), "opaque GEODATA save"); saved.Position = 0;
        var loaded = DxfDocument.Load(saved) ?? throw new Exception("Opaque reload failed."); var after = (DxfOpaqueObject)loaded.GetObjectByHandle(handle);
        Equal(tagsBefore.Length, after.Tags.Count, "opaque payload count");
        for (int i = 0; i < tagsBefore.Length; i++) { Equal(tagsBefore[i].Code, after.Tags[i].Code, "opaque code"); Equal(tagsBefore[i].Value, after.Tags[i].Value, "opaque value"); }
        int count = doc.Objects.Items.Count; Throws<NotSupportedException>(() => doc.Objects.CloneExtensionDictionary(GeoHost(doc), doc.Blocks.Add(new Block("OpaqueClone")).Record)); Equal(count, doc.Objects.Items.Count, "opaque clone leaves database unchanged");
    }
    private static void GeoDataMalformed(string defect)
    {
        var raw = GeoDataRaw(out string handle);
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            void Change(short code, object value) { int i = tags.FindIndex(t => t.Code == code); tags[i] = new DxfTag(code, value); }
            switch (defect)
            {
                case "point-count": Change(93, int.MaxValue); break;
                case "face-count": Change(96, 2); break;
                case "negative-count": Change(93, -1); break;
                case "face-index": Change(99, 3); break;
                case "missing-coordinate": tags.RemoveAt(tags.FindIndex(t => t.Code == 23)); break;
                case "missing-definition": tags.RemoveAt(tags.FindIndex(t => t.Code == 301)); break;
                case "duplicate-scalar": tags.Insert(tags.FindIndex(t => t.Code == 93), new DxfTag(40, 2.0)); break;
                case "wrong-host": int host = tags.FindLastIndex(t => t.Code == 330); tags[host] = new DxfTag(330, handle); break;
                case "late-chunk": tags.Insert(tags.FindIndex(t => t.Code == 301) + 1, new DxfTag(303, "late")); break;
                case "orphan-host": int orphan = tags.FindLastIndex(t => t.Code == 330); tags[orphan] = new DxfTag(330, "ABCDEF"); break;
            }
            return tags;
        });
        foreach (bool binary in new[] { false, true })
        {
            using var stream = new MemoryStream(); raw.Save(stream, binary); stream.Position = 0; bool rejected;
            try { rejected = DxfDocument.Load(stream) == null; } catch (FormatException) { rejected = true; }
            Check(rejected, "Malformed GEODATA was accepted: " + defect);
        }
    }
    private static void GeoDataVersionGate(DxfVersion version)
    {
        var doc = new DxfDocument(version); int before = doc.Objects.Items.Count; var data = new DxfGeoData(GeoHost(doc)); string seed = doc.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => doc.Objects.SetGeoData(data)); Equal(before, doc.Objects.Items.Count, "version gate database"); Equal(seed, doc.DrawingVariables.HandleSeed, "version gate seed");
        Check(data.Owner == null && data.Database == null && GeoHost(doc).ExtensionDictionary == null, "version gate attached data");
    }
}
