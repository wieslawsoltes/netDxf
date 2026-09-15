using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredDimAssocTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool inputBinary in new[] { false, true })
        {
            foreach (bool binary in new[] { false, true })
                Run($"dimassoc/native/{version}/{inputBinary}/{binary}", () => StoredDimAssocNative(version, inputBinary, binary));
            foreach (int fault in Enumerable.Range(0, 22))
                Run($"dimassoc/malformed/{version}/{inputBinary}/{fault}", () => StoredDimAssocMalformed(version, inputBinary, fault));
            Run($"dimassoc/numeric-spelling/{version}/{inputBinary}", () => StoredDimAssocNumericSpelling(version, inputBinary));
            Run($"dimassoc/opaque-removal/{version}/{inputBinary}", () => StoredDimAssocOpaqueRemoval(version, inputBinary));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (int variant in Enumerable.Range(0, 10)) Run($"dimassoc/opaque/{binary}/{variant}", () => StoredDimAssocOpaque(binary, variant));
            foreach (int scenario in Enumerable.Range(0, 16)) Run($"dimassoc/lifecycle/{binary}/{scenario}", () => StoredDimAssocLifecycle(binary, scenario));
            Run($"dimassoc/containing-block/{binary}", () => StoredDimAssocContainingBlock(binary));
        }
    }
    private static string StoredDimAssocHandle(DxfVersion version, string source)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/dimassoc/manifest.json"));
        return manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("year").GetInt32() == int.Parse(version.ToString()[7..])).GetProperty("handle_map").GetProperty(source).GetString()!;
    }
    private static byte[] StoredDimAssocInput(DxfVersion version, bool binary)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/dimassoc/manifest.json"));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("year").GetInt32() == int.Parse(version.ToString()[7..]));
        byte[] bytes = File.ReadAllBytes(Path.Combine("tests/fixtures/dimassoc", fixture.GetProperty("file").GetString()!));
        Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "DIMASSOC extracted source hash");
        if (!binary) return bytes;
        using var input = new MemoryStream(bytes); using var output = new MemoryStream(); DxfRawDocument.Load(input).Save(output, true); return output.ToArray();
    }
    private static DxfDocument StoredDimAssocLoad(byte[] bytes)
    { using var input = new MemoryStream(bytes); return DxfDocument.Load(input) ?? throw new Exception("DIMASSOC load failed"); }
    private static byte[] StoredDimAssocSave(DxfDocument doc, bool binary, string? name = null)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "DIMASSOC save failed");
        byte[] bytes = stream.ToArray(); if (name != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, name), bytes); return bytes;
    }
    private static void StoredDimAssocCheck(DxfDocument doc)
    {
        var associations = doc.Objects.Items.OfType<DxfStoredDimAssoc>().ToArray(); Equal(7, associations.Length, "native typed count");
        Equal(1, doc.Objects.Items.OfType<DxfOpaqueObject>().Count(o => o.CodeName == "DIMASSOC"), "native repeated path remains opaque");
        foreach (var association in associations)
        {
            Equal(doc.DrawingVariables.AcadVer, association.SourceVersion, "source version");
            var owner = (DxfDictionary)association.Owner;
            Check(ReferenceEquals(owner.Owner, association.Dimension) && ReferenceEquals(association.Dimension.ExtensionDictionary, owner), "native reciprocal dimension owner");
            Check(ReferenceEquals(owner["ACAD_DIMASSOC"], association) && owner.Entries.Single().IsHardOwner, "native hard owning dictionary entry");
            Check(association.PersistentReactors.Contains(owner) && association.Dimension.PersistentReactors.Contains(association), "native reactor backlinks");
            Equal(1 + association.PointReferences.Count, association.References.Count, "ordered dependency count");
            Check(association.PointReferences.Select(p => p.PointIndex).SequenceEqual(Enumerable.Range(0, 4).Where(i => (association.AssociativityMask & (1 << i)) != 0)), "point indexes correspond to stored mask");
            foreach (var point in association.PointReferences)
            {
                Check(ReferenceEquals(doc.GetObjectByHandle(point.Geometry.Handle), point.Geometry), "exact geometry identity");
                Check(point.OsnapType is 1 or 3 or 13 && point.SubentityType == 2, "qualified native scalar values");
            }
            Check(!association.IsTransSpace && association.RotatedDimensionType == 0, "native independent flags");
        }
        Check(associations.Any(a => a.AssociativityMask == 7) && associations.Any(a => a.AssociativityMask == 15), "multiple native point counts");
        Check(associations.SelectMany(a => a.PointReferences).Any(p => p.Point.Z == 2e50), "finite native sentinel retained");
        Equal(0, doc.Objects.Validate().Count, "native DIMASSOC database validation");
    }
    private static void StoredDimAssocNative(DxfVersion version, bool inputBinary, bool binary)
    {
        var doc = StoredDimAssocLoad(StoredDimAssocInput(version, inputBinary)); StoredDimAssocCheck(doc);
        var before = doc.Objects.Items.OfType<DxfStoredDimAssoc>().ToDictionary(a => a.Handle, a => a.Tags.Select(t => t.Code + ":" + t.Value).ToArray());
        byte[] bytes = StoredDimAssocSave(doc, binary, $"dimassoc-native-{version}-{inputBinary}-{binary}.dxf");
        var loaded = StoredDimAssocLoad(bytes); StoredDimAssocCheck(loaded);
        foreach (var association in loaded.Objects.Items.OfType<DxfStoredDimAssoc>())
            Check(before[association.Handle].SequenceEqual(association.Tags.Select(t => t.Code + ":" + t.Value)), "exact native subclass tags changed");
    }
    private static DxfRawDocument StoredDimAssocRaw(DxfVersion version, bool binary)
    { using var input = new MemoryStream(StoredDimAssocInput(version, binary)); return DxfRawDocument.Load(input); }
    private static byte[] StoredDimAssocRawBytes(DxfRawDocument raw, bool binary)
    { using var output = new MemoryStream(); raw.Save(output, binary); return output.ToArray(); }
    private static void StoredDimAssocMalformed(DxfVersion version, bool binary, int fault)
    {
        var raw = StoredDimAssocRaw(version, binary); string handle = StoredDimAssocHandle(version, "452");
        if (fault == 18 || fault == 19) handle = StoredDimAssocHandle(version, "448");
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int marker = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbDimAssoc");
            int at(short code) => tags.FindIndex(Math.Max(marker + 1, 0), t => t.Code == code);
            if (fault == 0) tags.RemoveAt(marker);
            else if (fault == 1) tags.Insert(marker, tags[marker]);
            else if (fault == 2) tags[at(90)] = new DxfTag(90, 16);
            else if (fault == 3) tags[at(90)] = new DxfTag(90, 7);
            else if (fault == 4) tags[at(70)] = new DxfTag(70, (short)2);
            else if (fault == 5) tags[at(71)] = new DxfTag(71, (short)-1);
            else if (fault == 6) tags.RemoveAt(at(40));
            else if (fault == 7) tags.Insert(at(40), tags[at(40)]);
            else if (fault == 8) tags.RemoveAt(at(30));
            else if (fault == 9) tags[at(330)] = new DxfTag(330, "0");
            else if (fault == 10) tags[at(330)] = new DxfTag(330, "FFFFFFFF");
            else if (fault == 11) tags[at(330)] = new DxfTag(330, StoredDimAssocHandle(version, "419"));
            else if (fault == 12) tags[at(331)] = new DxfTag(331, "0000000000000000");
            else if (fault == 13) tags[at(331)] = new DxfTag(331, "FFFFFFFF");
            else if (fault == 14) tags[at(331)] = new DxfTag(331, StoredDimAssocHandle(version, "448"));
            else if (fault == 15) tags[at(75)] = new DxfTag(75, (short)2);
            else if (fault == 16) tags[at(72)] = new DxfTag(72, (short)14);
            else if (fault == 17) tags.Insert(tags.FindIndex(t => t.Code == 5) + 1, new DxfTag(5, "ABC"));
            else if (fault == 18) tags[tags.FindIndex(t => t.Code == 360)] = new DxfTag(350, StoredDimAssocHandle(version, "452"));
            else if (fault == 19) tags[tags.FindIndex(t => t.Code == 3)] = new DxfTag(3, "OTHER");
            else if (fault == 20) tags.Insert(tags.FindIndex(t => t.Code == 102 && (string)t.Value == "{ACAD_REACTORS") + 1, new DxfTag(330, StoredDimAssocHandle(version, "448")));
            else tags[at(331)] = new DxfTag(331, StoredDimAssocHandle(version, "41E"));
            return tags;
        });
        bool rejected = false;
        try { using var input = new MemoryStream(StoredDimAssocRawBytes(raw, binary)); rejected = DxfDocument.Load(input) == null; }
        catch (FormatException) { rejected = true; }
        Check(rejected, "malformed DIMASSOC was accepted or made opaque");
    }
    private static void StoredDimAssocNumericSpelling(DxfVersion version, bool binary)
    {
        string handle = StoredDimAssocHandle(version, "452"); var raw = StoredDimAssocRaw(version, binary);
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            bool active = false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i].Code == 100) active = (string)tags[i].Value == "AcDbDimAssoc";
                if (active && tags[i].Code is 330 or 331) tags[i] = new DxfTag(tags[i].Code, "000" + ((string)tags[i].Value).ToLowerInvariant());
            }
            return tags;
        });
        var doc = StoredDimAssocLoad(StoredDimAssocRawBytes(raw, binary)); var association = (DxfStoredDimAssoc)doc.GetObjectByHandle(handle);
        Check(ReferenceEquals(association.Dimension, doc.GetObjectByHandle(StoredDimAssocHandle(version, "43B"))) && association.PointReferences.All(p => ReferenceEquals(p.Geometry, doc.GetObjectByHandle(StoredDimAssocHandle(version, "419")))), "noncanonical numeric pointers did not resolve exact source identities");
        var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, !binary));
        var copy = (DxfStoredDimAssoc)loaded.GetObjectByHandle(handle); Check(ReferenceEquals(copy.Dimension, loaded.GetObjectByHandle(association.Dimension.Handle)) && copy.PointReferences.All(p => ReferenceEquals(p.Geometry, loaded.GetObjectByHandle(association.PointReferences[0].Geometry.Handle))), "numeric source identity changed during save");
    }
    private static void StoredDimAssocContainingBlock(bool binary)
    {
        var raw = StoredDimAssocRaw(DxfVersion.AutoCad2018, binary);
        var tables = raw.Sections.Single(s => s.Name == "TABLES");
        var table = tables.Records.Single(r => r.Name == "TABLE" && r.Tags.Any(t => t.Code == 2 && (string)t.Value == "BLOCK_RECORD"));
        string tableHandle = (string)table.Tags.Single(t => t.Code == 5).Value;
        int tableEnd = tables.Records.First(r => r.StartTagIndex > table.StartTagIndex && r.Name == "ENDTAB").StartTagIndex;
        var blocks = raw.Sections.Single(s => s.Name == "BLOCKS"); var entities = raw.Sections.Single(s => s.Name == "ENTITIES");
        string oldOwner = StoredDimAssocHandle(DxfVersion.AutoCad2018, "1F");
        var moved = entities.Content.Select(t => t.Code == 330 && (string)t.Value == oldOwner ? new DxfTag(330, "F3000") : t).ToList();
        var record = new[] { new DxfTag(0,"BLOCK_RECORD"),new DxfTag(5,"F3000"),new DxfTag(330,tableHandle),new DxfTag(100,"AcDbSymbolTableRecord"),new DxfTag(100,"AcDbBlockTableRecord"),new DxfTag(2,"ASSOC_HOST"),new DxfTag(70,(short)0),new DxfTag(280,(short)1),new DxfTag(281,(short)0) };
        var begin = new[] { new DxfTag(0,"BLOCK"),new DxfTag(5,"F3001"),new DxfTag(330,"F3000"),new DxfTag(100,"AcDbEntity"),new DxfTag(8,"0"),new DxfTag(100,"AcDbBlockBegin"),new DxfTag(2,"ASSOC_HOST"),new DxfTag(70,(short)0),new DxfTag(10,0.0),new DxfTag(20,0.0),new DxfTag(30,0.0),new DxfTag(3,"ASSOC_HOST"),new DxfTag(1,"") };
        var end = new[] { new DxfTag(0,"ENDBLK"),new DxfTag(5,"F3002"),new DxfTag(330,"F3000"),new DxfTag(100,"AcDbEntity"),new DxfTag(8,"0"),new DxfTag(100,"AcDbBlockEnd") };
        var tags = new List<DxfTag>();
        for (int i = 0; i < raw.Tags.Count; i++)
        {
            if (i == tableEnd) tags.AddRange(record);
            if (i == blocks.EndTagIndex - 1) { tags.AddRange(begin); tags.AddRange(moved); tags.AddRange(end); }
            if (i < entities.ContentStartTagIndex || i >= entities.EndTagIndex - 1) tags.Add(raw.Tags[i]);
        }
        var doc = StoredDimAssocLoad(StoredDimAssocRawBytes(DxfRawDocument.Create(tags), binary));
        var host = doc.Blocks["ASSOC_HOST"]; var association = (DxfStoredDimAssoc)doc.GetObjectByHandle(StoredDimAssocHandle(DxfVersion.AutoCad2018,"452"));
        Check(ReferenceEquals(association.Dimension.Owner, host), "scoped host identity");
        Check(!doc.Blocks.Remove(host), "block owning a live association was removed");
        Check(!host.Entities.Remove(association.Dimension) && !host.Entities.Remove(association.PointReferences[0].Geometry), "block entity dependency removed");
        Equal(0, doc.Objects.Validate().Count, "containing-block rejection changed the graph");
    }
    private static void StoredDimAssocOpaque(bool binary, int variant)
    {
        const DxfVersion version = DxfVersion.AutoCad2018;
        string handle = StoredDimAssocHandle(version, "452"); var raw = StoredDimAssocRaw(version, binary);
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int marker = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbDimAssoc");
            int at(short code) => tags.FindIndex(marker + 1, t => t.Code == code);
            if (variant == 0) tags.Insert(at(331), new DxfTag(331, "EEEEFFFF"));
            else if (variant == 1) tags.Insert(at(75), new DxfTag(332, "EEEEFFFF"));
            else if (variant == 2) tags.Insert(at(40), new DxfTag(301, @"inert\unopened.dwg"));
            else if (variant == 3) tags.Insert(at(40), new DxfTag(302, @"inert\unopened2.dwg"));
            else if (variant == 4) tags[at(75)] = new DxfTag(75, (short)1);
            else if (variant == 5) { tags[at(1)] = new DxfTag(1, "PrivatePointRef"); tags[at(72)] = new DxfTag(72, (short)32767); }
            else if (variant == 6) tags[at(72)] = new DxfTag(72, (short)6);
            else if (variant == 7) tags.AddRange(new[] { new DxfTag(100, "PrivateDimAssoc"), new DxfTag(94, 17), new DxfTag(75, (short)17) });
            else if (variant == 8) tags.InsertRange(marker, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(1, "private"), new DxfTag(102, "}") });
            else tags[marker] = new DxfTag(100, "PrivateAssocBase");
            // This unresolved known pointer must not bind after whole-private fallback.
            tags[tags.FindIndex(marker + 1, t => t.Code == 330)] = new DxfTag(330, "FFFFFFFE");
            return tags;
        });
        var doc = StoredDimAssocLoad(StoredDimAssocRawBytes(raw, binary)); var association = (DxfOpaqueObject)doc.GetObjectByHandle(handle);
        var before = association.Tags.Select(t => t.Code + ":" + t.Value).ToArray();
        var destination = new DxfDocument(version); int count = destination.Objects.Items.Count();
        Throws<NotSupportedException>(() => destination.Objects.CloneObject(association, destination.NamedObjects, "COPY"));
        Equal(count, destination.Objects.Items.Count(), "opaque clone mutated destination");
        var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"dimassoc-opaque-{variant}-{binary}.dxf"));
        Check(before.SequenceEqual(((DxfOpaqueObject)loaded.GetObjectByHandle(handle)).Tags.Select(t => t.Code + ":" + t.Value)), "whole opaque packet changed");
    }
    private static void StoredDimAssocOpaqueRemoval(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(StoredDimAssocInput(version, binary));
        var association = (DxfOpaqueObject)doc.GetObjectByHandle(StoredDimAssocHandle(version,"42F"));
        var dimension = (Dimension)association.Owner.Owner;
        var geometry = (EntityObject)doc.GetObjectByHandle(StoredDimAssocHandle(version,"41A"));
        Check(!doc.Entities.Remove(dimension), "dimension owning opaque association removed");
        Check(!doc.Entities.Remove(geometry), "opaque exposed geometry pointer target removed");
        Equal(0, doc.Objects.Validate().Count, "opaque removal guard mutated registration");
    }
    private static void StoredDimAssocLifecycle(bool binary, int scenario)
    {
        const DxfVersion version = DxfVersion.AutoCad2018;
        var doc = StoredDimAssocLoad(StoredDimAssocInput(version, binary));
        var association = (DxfStoredDimAssoc)doc.GetObjectByHandle(StoredDimAssocHandle(version, "452")); var owner = (DxfDictionary)association.Owner;
        var geometry = association.PointReferences[0].Geometry; int count = doc.Objects.Items.Count();
        if (scenario == 0) Throws<NotSupportedException>(() => doc.Objects.CloneObject(association, doc.NamedObjects, "COPY"));
        else if (scenario == 1) Throws<NotSupportedException>(() => doc.Objects.CloneObject(owner, doc.NamedObjects, "COPY"));
        else if (scenario == 2)
        {
            var target = new DxfDocument(version); var dimension = new LinearDimension(Vector2.Zero, Vector2.UnitX, 1, 0); target.Entities.Add(dimension); int before = target.Objects.Items.Count();
            Throws<NotSupportedException>(() => target.Objects.CloneObject(association, target.NamedObjects, "COPY", new Dictionary<DxfObject,DxfObject> { [association.Dimension] = dimension }));
            Equal(before, target.Objects.Items.Count(), "cross-document rejected clone registered objects");
        }
        else if (scenario == 3) Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(association));
        else if (scenario == 4) Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(owner));
        else if (scenario == 5) Check(!doc.Entities.Remove(association.Dimension), "referenced dimension removed");
        else if (scenario == 6) Check(!doc.Entities.Remove(geometry), "referenced geometry removed");
        else if (scenario == 7) association.Dimension.PersistentReactors.Remove(association);
        else if (scenario == 8) association.PersistentReactors.Clear();
        else if (scenario == 9) owner.Remove("ACAD_DIMASSOC");
        else if (scenario == 10) { if (geometry.PersistentReactors.Contains(association)) geometry.PersistentReactors.Remove(association); else geometry.PersistentReactors.Add(association); }
        else if (scenario == 11) doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2013;
        else if (scenario == 12)
        {
            var app = new ApplicationRegistry("DIMASSOC_APP"); var data = new XData(app);
            data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 0, 128, 255 }));
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, geometry.Handle)); association.XData.Add(data);
            doc.ApplicationRegistries["DIMASSOC_APP"].Name = "RENAMED_ASSOC";
            Check(!doc.ApplicationRegistries.Remove("RENAMED_ASSOC"), "live APPID removed");
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"dimassoc-metadata-{binary}.dxf"));
            var copy = (DxfStoredDimAssoc)loaded.GetObjectByHandle(association.Handle);
            Check(((byte[])copy.XData["RENAMED_ASSOC"].XDataRecord[0].Value).SequenceEqual(new byte[] { 0, 128, 255 }), "association XData binary changed");
        }
        else if (scenario == 13) { doc.Classes.Remove("DIMASSOC"); StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); }
        else if (scenario == 14) { doc.Classes.Remove("DIMASSOC"); doc.Classes.Add(new DxfClass("DIMASSOC", "PrivateClass", "Private")); }
        else
        {
            var tags = (IList<DxfTag>)association.Tags; Throws<NotSupportedException>(() => tags.Clear());
            var points = (IList<DxfStoredDimAssocPoint>)association.PointReferences; Throws<NotSupportedException>(() => points.Clear());
            var refs = (IList<DxfObject>)association.References; Throws<NotSupportedException>(() => refs.Clear());
        }
        Equal(count, doc.Objects.Items.Count(), "rejection or read-only edit changed object count");
        if (scenario is >= 7 and <= 11 or 14)
        {
            using var output = new MemoryStream();
            if (scenario == 11)
            {
                Check(doc.Objects.Validate().Any(error => error.Contains("Stored DIMASSOC version conversion", StringComparison.Ordinal)), "DIMASSOC profile validation disappeared");
                // Retained native VERTEX records can reject the same profile change earlier.
                try { CheckSaveRejected(doc, output); } catch (NotSupportedException) { }
            }
            else CheckSaveRejected(doc, output);
            Equal(0L, output.Length, "invalid DIMASSOC preflight wrote bytes");
        }
        else { Check(ReferenceEquals(doc.GetObjectByHandle(association.Handle), association) && !association.IsErased, "association lost registration"); Equal(0, doc.Objects.Validate().Count, "valid lifecycle database"); }
    }
}
