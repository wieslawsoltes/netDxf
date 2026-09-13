using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRasterOwnershipTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (int scenario in new[] { 0, 1, 2 })
                {
                    DxfVersion v = version; bool b = binary; int s = scenario;
                    Run($"objects/raster-owner/{v}/{b}/{s}", () => RasterOwnership(v, b, s));
                }
    }

    private static void RasterOwnership(DxfVersion version, bool binary, int scenario)
    {
        foreach (ImageUnits units in Enum.GetValues<ImageUnits>())
        {
            var document = new DxfDocument(version);
            document.RasterVariables.DisplayFrame = ((int)units & 1) != 0;
            document.RasterVariables.DisplayQuality = ((int)units & 2) != 0 ? ImageDisplayQuality.High : ImageDisplayQuality.Draft;
            document.RasterVariables.Units = units;
            var metadata = new XData(new ApplicationRegistry("RASTER_OWNER_TEST"));
            metadata.XDataRecord.Add(new XDataRecord(XDataCode.String, "raster metadata"));
            document.RasterVariables.XData.Add(metadata);
            if (scenario != 0)
            {
                var definition = new ImageDefinition("RasterFixture", "not-loaded.png", 16, 96, 8, 96, ImageResolutionUnits.Inches);
                var image = new Image(definition, Vector3.Zero, 16, 8);
                if (scenario == 1) document.Entities.Add(image);
                else
                {
                    var block = new Block("NestedImage"); block.Entities.Add(image);
                    document.Entities.Add(new Insert(block, new Vector3(10, 20, 0)));
                    document.Entities.Add(new Image(definition, new Vector3(30, 40, 0), 16, 8));
                }
            }

            bool frame = document.RasterVariables.DisplayFrame;
            ImageDisplayQuality quality = document.RasterVariables.DisplayQuality;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                bool transport = cycle == 1 ? !binary : binary;
                using var output = new MemoryStream();
                Check(document.Save(output, transport), "Raster fixture save failed.");
                byte[] bytes = output.ToArray();
                // Keep one complete fixture per version/transport/scenario for independent readers.
                if (cycle == 0 && units == ImageUnits.Unitless)
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"raster-owner-{version}-{binary}-{scenario}.dxf"), bytes);
                CheckRasterObjectGraph(bytes, transport, document.RasterVariables.Handle, scenario == 0 ? 0 : 1);
                Check(output.CanWrite, "Raster save closed a caller-owned stream.");
                output.Position = 0;
                document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Raster fixture reload failed.");
                Check(output.CanRead, "Raster load closed a caller-owned stream.");
                Equal(frame, document.RasterVariables.DisplayFrame, "Raster display frame changed");
                Equal(quality, document.RasterVariables.DisplayQuality, "Raster quality changed");
                Equal(units, document.RasterVariables.Units, "Raster units changed");
                Equal("raster metadata", (string)document.RasterVariables.XData["RASTER_OWNER_TEST"].XDataRecord.Single().Value,
                    "Raster metadata changed");
                Equal(scenario == 0 ? 0 : 1, document.ImageDefinitions.Count, "Image definition count changed");
                if (scenario != 0)
                {
                    ImageDefinition definition = document.ImageDefinitions["RasterFixture"];
                    Equal(16, definition.Width, "Image definition width changed");
                    Equal(8, definition.Height, "Image definition height changed");
                    Equal("not-loaded.png", definition.File, "Image definition path changed");
                }
            }
        }
    }

    private static void CheckRasterObjectGraph(byte[] bytes, bool binary, string rasterHandle, int definitionCount)
    {
        using var input = new MemoryStream(bytes);
        object reader = NewCodeReader(input, binary);
        var records = new List<List<(short Code, object Value)>>();
        List<(short Code, object Value)>? record = null;
        bool objects = false, sectionName = false;
        while (true)
        {
            Invoke(reader, "Next"); short code = TagCode(reader);
            object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0)
            {
                if (record != null) { records.Add(record); record = null; }
                if (Equals(value, "EOF")) break;
                if (Equals(value, "SECTION")) { sectionName = true; continue; }
                if (Equals(value, "ENDSEC")) { objects = false; continue; }
                if (objects) record = new List<(short, object)>();
            }
            if (sectionName && code == 2) { objects = Equals(value, "OBJECTS"); sectionName = false; }
            record?.Add((code, value));
        }

        static string Type(List<(short Code, object Value)> tags) => (string)tags[0].Value;
        static string Handle(List<(short Code, object Value)> tags) => (string)tags.Single(t => t.Code == 5).Value;
        static string Owner(List<(short Code, object Value)> tags)
        {
            // Group 330 inside an application/reactor group is not the object's owner field.
            int depth = 0;
            foreach (var (code, value) in tags)
            {
                if (code == 102)
                {
                    string marker = (string)value;
                    if (marker.StartsWith("{", StringComparison.Ordinal)) depth++;
                    else if (marker == "}") depth--;
                }
                if (code == 330 && depth == 0) return (string)value;
            }
            throw new InvalidOperationException("Missing object owner field.");
        }
        static Dictionary<string, string> Entries(List<(short Code, object Value)> tags)
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            string? name = null;
            foreach (var (code, value) in tags)
            {
                if (code == 3) name = (string)value;
                else if (code is 350 or 360 && name != null) { entries.Add(name, (string)value); name = null; }
            }
            return entries;
        }

        var root = records.First(); Equal("DICTIONARY", Type(root), "First OBJECTS record must be the named object dictionary");
        string rootHandle = Handle(root);
        var rootEntries = Entries(root);
        string imageDictionaryHandle = rootEntries["ACAD_IMAGE_DICT"];
        Equal(rasterHandle, rootEntries["ACAD_IMAGE_VARS"], "Named dictionary lost raster variables reference");
        var raster = records.Single(r => Type(r) == "RASTERVARIABLES");
        Equal(rasterHandle, Handle(raster), "Raster object handle changed");
        Equal(rootHandle, Owner(raster), "RASTERVARIABLES owner must match the dictionary containing ACAD_IMAGE_VARS");
        Check(Owner(raster) != imageDictionaryHandle, "Raster variables incorrectly belong to the image-definition dictionary.");
        var imageDictionary = records.Single(r => Handle(r) == imageDictionaryHandle);
        Equal(rootHandle, Owner(imageDictionary), "Image dictionary owner changed");
        var imageEntries = Entries(imageDictionary);
        Equal(definitionCount, imageEntries.Count, "Image dictionary entries changed");
        var definitions = records.Where(r => Type(r) == "IMAGEDEF").ToArray();
        Equal(definitionCount, definitions.Length, "Image definition object count changed");
        foreach (var definition in definitions)
        {
            Equal(imageDictionaryHandle, Owner(definition), "IMAGEDEF must still belong to ACAD_IMAGE_DICT");
            Check(imageEntries.Values.Contains(Handle(definition)), "Image definition lost its dictionary entry.");
        }
    }
}
