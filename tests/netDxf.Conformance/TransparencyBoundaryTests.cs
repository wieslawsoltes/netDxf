using System.Globalization;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterTransparencyBoundaryTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            foreach (int count in new[] { 0, 1, 2 }) { int c = count; Run($"transparency/boundary/layer-{c}/{v}/{b}", () => TransparencyAncillary(v, b, c)); }
            foreach (int packed in new[] { 0x020000FE, 0x020000FF, unchecked((int)0x810012FF), 0 })
            { int p = packed; Run($"transparency/boundary/state-{p:X8}/{v}/{b}", () => TransparencyLayerState(v, b, p)); }
            Run($"transparency/boundary/authored/{v}/{b}", () => TransparencyAuthored(v, b));
        }
    }
    private static DxfRawDocument TransparencyRaw(DxfDocument doc, bool binary)
    { using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Transparency boundary save"); bytes.Position = 0; return DxfRawDocument.Load(bytes); }
    private static DxfDocument TransparencyLoad(DxfRawDocument raw, bool binary)
    { using var bytes = new MemoryStream(); raw.Save(bytes, binary); bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new Exception("Transparency boundary load"); }
    private static DxfRawRecord TransparencyLayer(DxfRawDocument raw, string name)
        => raw.Sections.Single(s => s.Name == "TABLES").Records.Single(r => r.Name == "LAYER" && r.Tags.Any(t => t.Code == 2 && Equals(t.Value, name)));
    private static List<DxfTag> TransparencyApp(DxfRawRecord record)
        => record.Tags.SkipWhile(t => t.Code != 1001 || !Equals(t.Value, "AcCmTransparency")).Skip(1).TakeWhile(t => t.Code != 1001).ToList();
    private static string TransparencyPacket(IEnumerable<DxfTag> tags)
        => string.Join("|", tags.Select(t => t.Code + ":" + (t.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(t.Value, CultureInfo.InvariantCulture))));
    private static void TransparencyAncillary(DxfVersion version, bool binary, int slots)
    {
        var seed = new DxfDocument(version); seed.Layers.Add(new Layer("PRIVATE_ALPHA")); var raw = TransparencyRaw(seed, binary); var record = TransparencyLayer(raw, "PRIVATE_ALPHA");
        var packet = new List<DxfTag> { new(1000, "private-before"), new(1004, new byte[] { 3, 0, 255 }) };
        if (slots == 2) packet.Add(new DxfTag(1071, 0x02000080));
        if (slots != 0) packet.Add(new DxfTag(1071, 0x020000FF));
        packet.Add(new DxfTag(1040, 1.25)); packet.Add(new DxfTag(1000, "private-after"));
        var doc = TransparencyLoad(raw.WithRecord(record, record.Tags.Concat(new[] { new DxfTag(1001, "AcCmTransparency") }).Concat(packet)), binary);
        var layer = doc.Layers["PRIVATE_ALPHA"]; Equal(slots == 0 ? (int?)null : 0x020000FF, layer.Transparency.StoredAlphaValue, "Last1071 projection/presence");
        Equal(TransparencyPacket(packet), TransparencyPacket(TransparencyApp(TransparencyLayer(TransparencyRaw(doc, binary), layer.Name))), "Untouched ancillary packet");
        var copy = (Layer)layer.Clone(); copy.Name = "PRIVATE_ALPHA_COPY"; doc.Layers.Add(copy);
        Equal(TransparencyPacket(packet), TransparencyPacket(TransparencyApp(TransparencyLayer(TransparencyRaw(doc, binary), copy.Name))), "Cloned ancillary packet");
        Throws<ArgumentOutOfRangeException>(() => layer.Transparency.Value = -1);
        Equal(TransparencyPacket(packet), TransparencyPacket(TransparencyApp(TransparencyLayer(TransparencyRaw(doc, binary), layer.Name))), "Failed edit changed ancillary packet");
        foreach (short value in new short[] { 0, 25 })
        {
            layer.Transparency.Value = value; int index = packet.FindLastIndex(t => t.Code == 1071); var replacement = new DxfTag(1071, Transparency.ToAlphaValue(new Transparency(value)));
            if (index < 0) packet.Add(replacement); else packet[index] = replacement;
            Equal(TransparencyPacket(packet), TransparencyPacket(TransparencyApp(TransparencyLayer(TransparencyRaw(doc, binary), layer.Name))), "Edit must preserve every ancillary tag and earlier1071 slot");
            var loaded = TransparencyLoad(TransparencyRaw(doc, binary), binary); Equal(value, loaded.Layers[layer.Name].Transparency.Value, "Edited ancillary transparency reload");
        }
    }
    private static void TransparencyLayerState(DxfVersion version, bool binary, int packed)
    {
        var doc = new DxfDocument(version); var layer = new Layer("STATE_ALPHA_LAYER") { Transparency = Transparency.FromAlphaValue(packed) }; doc.Layers.Add(layer); doc.Layers.StateManager.AddNew("STATE_ALPHA");
        var raw = TransparencyRaw(doc, binary); var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "XRECORD" && r.Tags.Any(t => t.Code == 440));
        var tags = record.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 330 && Equals(t.Value, layer.Handle)); int slot = tags.FindIndex(start, t => t.Code == 440);
        Equal(packed, (int)tags[slot].Value, "Authored imported state exact alpha");
        // The physical input is set explicitly so the reader is checked independently of the writer.
        tags[slot] = new DxfTag(440, packed); var loaded = TransparencyLoad(raw.WithRecord(record, tags), binary); var alpha = loaded.Layers.StateManager["STATE_ALPHA"].Properties[layer.Name].Transparency;
        Equal((int?)packed, alpha.StoredAlphaValue, "Layer-state input alpha presence"); if (packed == 0) Equal((short)0, alpha.Value, "Layer-state numeric zero effective opaque contract");
        Equal((int?)packed, ((LayerStateProperties)loaded.Layers.StateManager["STATE_ALPHA"].Properties[layer.Name].Clone()).Transparency.StoredAlphaValue, "Layer-state clone packed alpha");
        var saved = TransparencyRaw(loaded, binary); var output = saved.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "XRECORD" && r.Tags.Any(t => t.Code == 440));
        var outputTags = output.Tags.ToList(); int owner = outputTags.FindIndex(t => t.Code == 330 && Equals(t.Value, layer.Handle)); Equal(packed, (int)outputTags[outputTags.FindIndex(owner, t => t.Code == 440)].Value, "Layer-state exact alpha reload/save");
    }
    private static void TransparencyAuthored(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.Layers.Add(new Layer("DEFAULT_ALPHA")); doc.Layers.StateManager.AddNew("DEFAULT_ALPHA_STATE"); var raw = TransparencyRaw(doc, binary);
        Check(!TransparencyLayer(raw, "DEFAULT_ALPHA").Tags.Any(t => t.Code == 1001 && Equals(t.Value, "AcCmTransparency")), "Default authored layer acquired transparency XData");
        Check(raw.Sections.Single(s => s.Name == "OBJECTS").Records.Where(r => r.Name == "XRECORD").SelectMany(r => r.Tags).Where(t => t.Code == 440).All(t => Equals(t.Value, 0)), "Authored state opaque encoding changed");
    }
}
