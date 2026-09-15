using System.IO.Compression;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunEntitySourceIdentityTests()
    {
        foreach (bool binary in new[] { false, true })
        foreach (string kind in new[] { "LINE", "POLYLINE", "SECTIONOBJECT" })
        foreach (string fault in new[] { "missing", "duplicate-same", "zero" })
            Run($"source-entity/identity/{kind}/{fault}/{binary}", () => EntitySourceIdentityMalformed(kind, fault, binary));
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"source-entity/valid-sequences/{version}/{binary}", () => EntitySourceIdentitySequences(version, binary));
    }

    private static DxfRawDocument EntitySourceIdentityRaw(string kind)
    {
        using var bytes = new MemoryStream();
        if (kind == "SECTIONOBJECT")
        {
            using var file = File.OpenRead("tests/fixtures/section/LiveSection1.dxf.gz");
            using var gzip = new GZipStream(file, CompressionMode.Decompress); gzip.CopyTo(bytes);
        }
        else
        {
            var doc = new DxfDocument(DxfVersion.AutoCad2018);
            if (kind == "LINE") doc.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
            else doc.Entities.Add(new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }));
            Check(doc.Save(bytes), "Identity test seed save");
        }
        bytes.Position = 0; return DxfRawDocument.Load(bytes);
    }

    private static void EntitySourceIdentityMalformed(string kind, string fault, bool binary)
    {
        var raw = EntitySourceIdentityRaw(kind);
        var record = raw.Sections.Single(s => s.Name == "ENTITIES").Records.First(r => r.Name == kind);
        var tags = record.Tags.ToList(); int index = tags.FindIndex(t => t.Code == 5);
        if (fault == "missing") tags.RemoveAt(index);
        else if (fault == "duplicate-same") tags.Insert(index, tags[index]);
        else tags[index] = new DxfTag(5, "0");
        raw = raw.WithRecord(record, tags);
        using var bytes = new MemoryStream(); raw.WithTags(raw.Tags.Where(t => t.Code != 999)).Save(bytes, binary); bytes.Position = 0;
        bool rejected = false;
        try { rejected = DxfDocument.Load(bytes) == null; }
        catch (FormatException) { rejected = true; }
        Check(rejected, "Malformed retained entity identity must reject through the public load error path");
    }

    private static void EntitySourceIdentitySequences(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version);
        doc.Entities.Add(new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }));
        var block = new Block("IDENTITY_SEQUENCE"); block.AttributeDefinitions.Add(new AttributeDefinition("VALUE"));
        doc.Entities.Add(new Insert(block));
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Valid sequence save"); bytes.Position = 0;
        var loaded = DxfDocument.Load(bytes) ?? throw new FormatException("Valid entity sequence rejected");
        Equal(3, loaded.Entities.Polylines3D.Single().Vertexes.Count, "Valid POLYLINE/VERTEX sequence changed");
        Equal(1, loaded.Entities.Inserts.Single().Attributes.Count, "Valid INSERT/ATTRIB/SEQEND sequence changed");
    }
}
