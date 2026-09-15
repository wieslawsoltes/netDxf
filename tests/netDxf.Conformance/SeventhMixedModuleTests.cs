using System.Globalization;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static byte[] SeventhBytes(DxfDocument doc, bool binary)
    {
        using var bytes = new MemoryStream();
        Check(doc.Save(bytes, binary), "Seventh mixed save");
        return bytes.ToArray();
    }

    private static DxfDocument SeventhLoad(DxfRawDocument raw, bool binary)
    {
        using var bytes = new MemoryStream(); raw.Save(bytes, binary); bytes.Position = 0;
        return DxfDocument.Load(bytes) ?? throw new FormatException("Seventh mixed carrier failed to load");
    }

    private static DxfRawRecord SeventhRecord(DxfRawDocument raw, string handle)
        => raw.Sections.SelectMany(section => section.Records).Single(record =>
            record.Tags.TakeWhile(tag => tag.Code != 100).Any(tag => tag.Code == 5 && Equals(tag.Value, handle)));

    private static string SeventhHandle(DxfRawRecord record)
        => (string)record.Tags.TakeWhile(tag => tag.Code != 100).Single(tag => tag.Code == 5).Value;

    // These application packets are the native records already selected and
    // uniformly remapped by the independently checked DIMASSOC extractor. The
    // named resources and empty dimension display blocks are synthetic scaffolds.
    // When embedding them, only their external model-space owner is rebound.
    private static DxfDocument SeventhAppendDimAssoc(DxfDocument doc, bool binary)
    {
        DxfVersion version = doc.DrawingVariables.AcadVer;
        var source = StoredDimAssocRaw(version, binary);
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/dimassoc/manifest.json"));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray()
            .Single(item => item.GetProperty("year").GetInt32() == int.Parse(version.ToString()[7..], CultureInfo.InvariantCulture));
        var map = fixture.GetProperty("handle_map");
        var handles = fixture.GetProperty("application_packets").EnumerateArray()
            .Select(item => map.GetProperty(item.GetString()!).GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var application = source.Sections.SelectMany(section => section.Records)
            .Where(record => record.Tags.TakeWhile(tag => tag.Code != 100)
                .Any(tag => tag.Code == 5 && handles.Contains((string)tag.Value))).ToArray();
        Equal(32, application.Length, "Complete native DIMASSOC application packet inventory");
        Check(handles.All(handle => doc.GetObjectByHandle(handle) == null), "Mixed carrier collides with native DIMASSOC identities");

        var entityPackets = application.Where(record => record.Name is not "DIMASSOC" and not "DICTIONARY").ToArray();
        var displayBlocks = new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in entityPackets)
        {
            foreach (string name in record.Tags.Where(tag => tag.Code == 8).Select(tag => (string)tag.Value).Distinct())
                if (!doc.Layers.Contains(name)) doc.Layers.Add(new Layer(name));
            if (record.Name is "DIMENSION" or "ARC_DIMENSION")
            {
                string block = (string)record.Tags.Single(tag => tag.Code == 2).Value;
                string style = (string)record.Tags.Single(tag => tag.Code == 3).Value;
                if (!displayBlocks.ContainsKey(block))
                {
                    Check(!doc.Blocks.Contains(block), "DIMASSOC scaffold name collides with a native TABLE block");
                    displayBlocks.Add(block, doc.Blocks.Add(new Block("SEVENTH_DISPLAY_" + displayBlocks.Count)));
                }
                if (!doc.DimensionStyles.Contains(style)) doc.DimensionStyles.Add(new DimensionStyle(style));
            }
            foreach (string name in record.Tags.Where(tag => tag.Code == 1001).Select(tag => (string)tag.Value).Distinct())
                if (!doc.ApplicationRegistries.Contains(name)) doc.ApplicationRegistries.Add(new ApplicationRegistry(name));
        }

        string sourceModel = map.GetProperty("1F").GetString()!;
        string targetModel = doc.Layouts["Model"].AssociatedBlock.Record.Handle;
        IEnumerable<DxfTag> Rebind(DxfRawRecord record) => record.Tags.Select(tag =>
            tag.Code == 330 && Equals(tag.Value, sourceModel) ? new DxfTag(330, targetModel) : tag);
        using var seed = new MemoryStream(SeventhBytes(doc, binary));
        var raw = DxfRawDocument.Load(seed);
        // Public authoring rejects anonymous names. Rename only these empty
        // scaffold records on the raw carrier; no native DIMENSION name changes.
        foreach (var pair in displayBlocks)
        {
            var record = SeventhRecord(raw, pair.Value.Record.Handle);
            raw = raw.WithRecord(record, record.Tags.Select(tag => tag.Code == 2 ? new DxfTag(2, pair.Key) : tag));
            var begin = raw.Sections.Single(section => section.Name == "BLOCKS").Records.Single(record => record.Name == "BLOCK" &&
                record.Tags.Any(tag => tag.Code == 330 && Equals(tag.Value, pair.Value.Record.Handle)));
            raw = raw.WithRecord(begin, begin.Tags.Select(tag => tag.Code is 2 or 3 ? new DxfTag(tag.Code, pair.Key) : tag));
        }
        int entitiesEnd = raw.Sections.Single(section => section.Name == "ENTITIES").EndTagIndex - 1;
        int objectsEnd = raw.Sections.Single(section => section.Name == "OBJECTS").EndTagIndex - 1;
        var tags = new List<DxfTag>();
        for (int index = 0; index < raw.Tags.Count; index++)
        {
            if (index == entitiesEnd) tags.AddRange(entityPackets.SelectMany(Rebind));
            if (index == objectsEnd) tags.AddRange(application.Where(record => record.Name is "DIMASSOC" or "DICTIONARY").SelectMany(Rebind));
            tags.Add(raw.Tags[index]);
        }
        int handseed = tags.FindIndex(tag => tag.Code == 9 && Equals(tag.Value, "$HANDSEED")) + 1;
        Check(handseed > 0 && tags[handseed].Code == 5, "Mixed header has one usable allocation seed");
        ulong minimum = handles.Max(handle => ulong.Parse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture)) + 1;
        if (ulong.Parse((string)tags[handseed].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) < minimum)
            tags[handseed] = new DxfTag(5, minimum.ToString("X", CultureInfo.InvariantCulture));
        raw = raw.WithTags(tags);
        foreach (var record in application)
            Check(OwnershipTagValues(Rebind(record)).SequenceEqual(OwnershipTagValues(SeventhRecord(raw, SeventhHandle(record)).Tags)),
                "Native DIMASSOC packet changed beyond its declared external owner mapping");
        return SeventhLoad(raw, binary);
    }
}
