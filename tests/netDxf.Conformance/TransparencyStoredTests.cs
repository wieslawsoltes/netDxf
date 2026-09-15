using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly int[] TransparencyWireValues = { 0x02000000, 0x02000001, 0x02000018, 0x02000080, 0x020000FE, 0x020000FF, 0x01000000, unchecked((int)0x8100127F) };
    private static void RegisterTransparencyStoredTests()
    {
        foreach (int mode in new[] { 0x01000000, 0x02000000 }) for (int alpha = 0; alpha < 256; alpha++)
        { int packed = mode | alpha; Run($"transparency/stored/packed-{packed:X8}", () => TransparencyStoredSample(packed)); }
        foreach (int value in new[] { 0, -1, int.MinValue, int.MaxValue, 0x0300007F, 0x00123456, unchecked((int)0xA5123481), 0x42000000 })
        { int packed = value; Run($"transparency/stored/unknown-{packed:X8}", () => TransparencyStoredSample(packed)); }
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true }) foreach (string kind in new[] { "LINE", "MTEXT", "LAYER" })
        { var v = version; bool b = binary; string k = kind; Run($"transparency/stored/wire/{k}/{v}/{b}", () => TransparencyStoredWire(v, b, k)); }
        Run("transparency/stored/authored-defaults", () =>
        {
            Check(new Transparency().StoredAlphaValue == null && new Transparency(0).StoredAlphaValue == null && Transparency.ByBlock.StoredAlphaValue == null && Transparency.ByLayer.StoredAlphaValue == null, "Authoring materialized packed input");
            Equal(0x01000000, Transparency.ToAlphaValue(Transparency.ByBlock), "Authored ByBlock encoding"); Equal(0x020000FF, Transparency.ToAlphaValue(new Transparency(0)), "Authored opaque encoding");
        });
    }
    private static void TransparencyStoredSample(int packed)
    {
        var item = Transparency.FromAlphaValue(packed); Equal((int?)packed, item.StoredAlphaValue, "Imported packed alpha"); Equal(packed, Transparency.ToAlphaValue(item), "Exact packed-alpha export");
        short percentage = (short)(100 - ((packed & 255) / 255.0) * 100); var legacy = Transparency.FromCadIndex(percentage);
        Equal(legacy.Value, item.Value, "Legacy effective percentage"); Equal(legacy.IsByBlock, item.IsByBlock, "Legacy ByBlock interpretation"); Check(item.Equals(legacy), "Legacy equality changed");
        var clone = (Transparency)item.Clone(); Equal((int?)packed, clone.StoredAlphaValue, "Clone packed alpha"); Equal(packed, Transparency.ToAlphaValue(clone), "Clone exact export");
        Throws<ArgumentOutOfRangeException>(() => item.Value = -1); Throws<ArgumentOutOfRangeException>(() => item.Value = 91); Equal((int?)packed, item.StoredAlphaValue, "Failed edit discarded packed alpha");
        item.Value = 25; Check(item.StoredAlphaValue == null, "Successful edit retained stale packed alpha"); Equal(Transparency.ToAlphaValue(new Transparency(25)), Transparency.ToAlphaValue(item), "Edited value encoding"); Equal((int?)packed, clone.StoredAlphaValue, "Edit changed clone packed alpha");
    }
    private static void TransparencyStoredWire(DxfVersion version, bool binary, string kind)
    {
        var doc = new DxfDocument(version);
        for (int i = 0; i < TransparencyWireValues.Length; i++)
        {
            var alpha = Transparency.FromAlphaValue(TransparencyWireValues[i]);
            if (kind == "LAYER")
            {
                var layer = new Layer("PACKED_" + i) { Transparency = alpha }; doc.Layers.Add(layer);
                var copy = (Layer)layer.Clone(); copy.Name = "PACKED_COPY_" + i; doc.Layers.Add(copy);
            }
            else
            {
                EntityObject entity = kind == "LINE" ? new Line(new Vector3(i, 0, 0), new Vector3(i, 1, 0)) : new MText("packed " + i, new Vector3(i, 0, 0), 1, 5);
                entity.Transparency = alpha; doc.Entities.Add(entity); doc.Entities.Add((EntityObject)entity.Clone());
            }
        }
        byte[] Save(DxfDocument drawing) { using var stream = new MemoryStream(); Check(drawing.Save(stream, binary), "Packed alpha wire save"); return stream.ToArray(); }
        var data = Save(doc); CheckTransparencyWire(data, kind, false);
        var loaded = DxfDocument.Load(new MemoryStream(data)) ?? throw new Exception("Packed alpha load"); CheckTransparencyWire(Save(loaded), kind, false);
        if (kind == "LAYER")
            for (int i = 0; i < TransparencyWireValues.Length; i++)
            { Equal((int?)TransparencyWireValues[i], loaded.Layers["PACKED_" + i].Transparency.StoredAlphaValue, "Layer packed alpha reload"); loaded.Layers["PACKED_" + i].Transparency.Value = 0; loaded.Layers["PACKED_COPY_" + i].Transparency.Value = 0; }
        else
            foreach (var entity in loaded.Entities.All) { Check(entity.Transparency.StoredAlphaValue.HasValue, "Entity lost packed-alpha presence"); entity.Transparency.Value = 0; }
        CheckTransparencyWire(Save(loaded), kind, true);
    }
    private static void CheckTransparencyWire(byte[] bytes, string kind, bool edited)
    {
        var raw = DxfRawDocument.Load(new MemoryStream(bytes));
        var packets = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == kind && (kind != "LAYER" || r.Tags.Any(t => t.Code == 2 && ((string)t.Value).StartsWith("PACKED_", StringComparison.Ordinal)))).ToArray();
        Equal(TransparencyWireValues.Length * 2, packets.Length, "Packed-alpha wire inventory");
        var actual = packets.Select(record => kind == "LAYER" ? (int)record.Tags.Single(t => t.Code == 1071).Value : (int)record.Tags.Single(t => t.Code == 440).Value).OrderBy(v => v).ToArray();
        var expected = TransparencyWireValues.SelectMany(v => new[] { edited ? 0x020000FF : v, edited ? 0x020000FF : v }).OrderBy(v => v).ToArray();
        Check(actual.SequenceEqual(expected), "Packed-alpha values changed on actual " + kind + " wire");
    }
}
