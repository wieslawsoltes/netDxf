// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly double[] MTextOrderAngles = { 37, 68, 68, 37, 345, 123, 330, 180 };
    private static Vector3 MTextOrderNormal(int index) => index switch
    {
        0 => Vector3.UnitZ, 1 => -Vector3.UnitZ, 2 => Vector3.UnitY,
        _ => new Vector3(2.0 / 7, 3.0 / 7, 6.0 / 7)
    };

    // Explicit bases independent of the reader's MathHelper.Transform call.
    private static Vector3 MTextOrderDirection(int normal, double degrees)
    {
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
        double q = Math.Sqrt(13);
        return normal switch
        {
            0 => new(c, s, 0), 1 => new(-c, s, 0), 2 => new(-c, 0, s),
            _ => new(-3 * c / q - 12 * s / (7 * q), 2 * c / q - 18 * s / (7 * q), 13 * s / (7 * q))
        };
    }

    private static List<DxfTag> MTextOrderSequence(int normal, int sample)
    {
        var tags = new List<DxfTag>();
        void A(double degrees) => tags.Add(new DxfTag(50, degrees));
        void V(double degrees)
        {
            Vector3 v = MTextOrderDirection(normal, degrees);
            tags.Add(new DxfTag(11, v.X)); tags.Add(new DxfTag(21, v.Y)); tags.Add(new DxfTag(31, v.Z));
        }
        switch (sample)
        {
            case 0: A(37); break;
            case 1: V(68); break;
            case 2: A(37); V(68); break;
            case 3: V(68); A(37); break;
            case 4: A(37); V(68); A(-15); break;
            case 5: V(68); A(37); V(123); break;
            case 6: A(-725); V(-30); break;
            case 7: V(68); A(360); V(180); break;
        }
        return tags;
    }

    private static void RegisterMTextOrientationOrderTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            foreach (string context in new[] { "plain", "nested", "direct", "embedded" })
            {
                if (context == "direct" && version < DxfVersion.AutoCad2007) continue;
                if (context == "embedded" && version < DxfVersion.AutoCad2018) continue;
                for (int normal = 0; normal < 4; normal++) for (int sample = 0; sample < MTextOrderAngles.Length; sample++)
                {
                    int n = normal, s = sample;
                    Run($"mtext-orientation/{version}/{binary}/{context}/{n}/{s}", () => MTextOrientationOrderWire(version, binary, context, n, s));
                }
            }
    }

    private static MText MTextOrderEntity(DxfDocument doc, string context) => context == "nested"
        ? doc.Blocks["ORIENTATION_BLOCK"].Entities.OfType<MText>().Single() : doc.Entities.MTexts.Single();

    private static void MTextOrientationOrderWire(DxfVersion version, bool binary, string context, int normal, int sample)
    {
        var document = new DxfDocument(version);
        var text = new MText("orientation\\Pnext", new Vector3(10, 20, 30), 2.5, 41)
        { Normal = MTextOrderNormal(normal), AttachmentPoint = MTextAttachmentPoint.MiddleRight, LineSpacingFactor = 1.5 };
        if (context == "direct" || context == "embedded")
        {
            text.Columns = ColumnModel(2, context == "direct" ? MTextColumnStorage.Direct : MTextColumnStorage.Embedded);
            if (context == "embedded") text.Columns.EmbeddedTextDirection = MTextOrderDirection(normal, MTextOrderAngles[sample]);
        }
        var xdata = new XData(new ApplicationRegistry("ORIENT_APP"));
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.String, "tail")); text.XData.Add(xdata);
        if (context == "nested")
        {
            var block = new Block("ORIENTATION_BLOCK"); block.Entities.Add(text); document.Entities.Add(new Insert(block));
        }
        else document.Entities.Add(text);
        document.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        using var seed = new MemoryStream(); Check(document.Save(seed, binary), "Orientation seed save failed"); seed.Position = 0;
        var raw = DxfRawDocument.Load(seed);
        var record = raw.Sections.SelectMany(section => section.Records).Single(r => r.Name == "MTEXT");
        var packet = new List<DxfTag>(); bool secondary = false, added = false;
        foreach (DxfTag tag in record.Tags)
        {
            if (tag.Code == 75 || tag.Code == 101 || tag.Code == 1001) secondary = true;
            if (!secondary && (tag.Code == 11 || tag.Code == 21 || tag.Code == 31 || tag.Code == 50))
            {
                if (!added) { packet.AddRange(MTextOrderSequence(normal, sample)); added = true; }
                continue;
            }
            packet.Add(tag);
        }
        Check(added, "Seed has no orientation insertion point");
        var tags = raw.Tags.Take(record.StartTagIndex).Concat(packet).Concat(raw.Tags.Skip(record.EndTagIndex));
        using var input = new MemoryStream(); raw.WithTags(tags).Save(input, binary);
        string name = $"mtext-orientation-{version}-{binary}-{context}-{normal}-{sample}";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, name + "-input.dxf"), input.ToArray());
        input.Position = 0; document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Orientation load failed");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            MText actual = MTextOrderEntity(document, context);
            Near(MTextOrderAngles[sample], actual.Rotation, "Last orientation did not win");
            Equal(text.Value, actual.Value, "Orientation changed text"); Equal(text.Position, actual.Position, "Orientation changed position");
            Near(2.5, actual.Height, "Orientation changed height"); Near(41, actual.RectangleWidth, "Orientation changed width");
            Near(1.5, actual.LineSpacingFactor, "Orientation changed spacing"); Equal(text.AttachmentPoint, actual.AttachmentPoint, "Orientation changed attachment");
            Check((actual.Normal - text.Normal).Modulus() < 1e-13, "Orientation changed extrusion");
            Equal("tail", (string)actual.XData["ORIENT_APP"].XDataRecord.Single().Value, "Trailing XData lost");
            Equal(1, document.Entities.Lines.Count(), "Following entity lost");
            if (text.Columns != null) { CheckColumns(actual, 2); Equal(text.Columns.Storage, actual.Columns!.Storage, "Column representation changed"); }
            if (cycle == 2) break;
            using var output = new MemoryStream(); Check(document.Save(output, !binary), "Orientation roundtrip save failed");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, name + "-output.dxf"), output.ToArray());
            output.Position = 0; document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Orientation roundtrip load failed"); binary = !binary;
        }
    }
}
