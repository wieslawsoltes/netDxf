// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static string[] MTextUnicodeSamples()
    {
        var values = new List<string>();
        foreach (int count in new[] { 0, 1, 248, 249, 250, 251, 499, 500, 501 }) values.Add(new string('a', count));
        foreach (int prefix in new[] { 0, 1, 246, 247, 248, 249, 250, 497, 498, 499 })
            values.Add(new string('x', prefix) + "\U0001F680" + new string('y', 503));
        values.Add(string.Concat(Enumerable.Repeat("Zażółć gęślą jaźń 東京 \U0001F680 e\u0301 ", 31)));
        values.Add(string.Concat(Enumerable.Repeat("\U00020000\U0010FFFF", 130)));
        values.Add(new string('\u6771', 333));
        values.Add(string.Concat(Enumerable.Repeat("\u00e9", 257)));
        values.Add(new string('a', 248) + "\\P{\\C1;red}\\S1/2;end");
        return values.ToArray();
    }

    private static void RegisterMTextUnicodeChunkTests()
    {
        string[] samples = MTextUnicodeSamples();
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            for (int i = 0; i < samples.Length; i++)
            {
                int sample = i;
                Run($"mtext-unicode/wire/{version}/{binary}/{i}", () => MTextUnicodeChunkWire(version, binary, sample, samples[sample], false));
            }
            Run($"mtext-unicode/nested/{version}/{binary}", () => MTextUnicodeChunkWire(version, binary, 0, samples[19], true));
            Run($"mtext-unicode/columns/{version}/{binary}", () => MTextUnicodeChunkColumns(version, binary, samples[19]));
        }
        foreach (bool binary in new[] { false, true })
            Run($"mtext-unicode/large/{binary}", () => MTextUnicodeChunkWire(DxfVersion.AutoCad2018, binary, 0,
                string.Concat(Enumerable.Repeat("x\U0001F680東京", 32768)), false, true));
    }

    private static void MTextUnicodeChunkWire(DxfVersion version, bool binary, int sample, string value, bool nested, bool large = false)
    {
        var document = new DxfDocument(version);
        var text = new MText(value, new Vector3(2, -3, 4), 2.5) { RectangleWidth = 40, Rotation = 23 };
        if (nested)
        {
            var block = new Block("UNICODE_BLOCK"); block.Entities.Add(text); document.Entities.Add(new Insert(block));
        }
        else document.Entities.Add(text);
        document.Entities.Add(new Line(new Vector3(8, 9, 10), new Vector3(11, 12, 13)));
        var app = new netDxf.Tables.ApplicationRegistry("CHUNK_APP");
        var data = new XData(app); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "tail")); text.XData.Add(data);
        string name = large ? $"mtext-unicode-large-{binary}.dxf" : nested ? $"mtext-unicode-nested-{version}-{binary}.dxf" : $"mtext-unicode-{version}-{binary}-{sample}.dxf";
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Unicode MTEXT save failed");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, name), stream.ToArray());
            stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Unicode MTEXT load failed");
            MText actual = nested ? loaded.Blocks["UNICODE_BLOCK"].Entities.OfType<MText>().Single() : loaded.Entities.MTexts.Single();
            Equal(value, actual.Value, "Unicode MTEXT text changed");
            Equal("tail", (string)actual.XData["CHUNK_APP"].XDataRecord.Single().Value, "Trailing XData changed");
            Equal(1, loaded.Entities.Lines.Count(), "Following entity lost");
            stream.Position = 0; var raw = DxfRawDocument.Load(stream);
            var packet = raw.Sections.SelectMany(section => section.Records).Single(record => record.Name == "MTEXT").Tags.Where(tag => tag.Code == 1 || tag.Code == 3).ToArray();
            Check(packet.Length > 0 && packet[^1].Code == 1 && packet.Take(packet.Length - 1).All(tag => tag.Code == 3), "Text chunk ordering changed");
            foreach (DxfTag tag in packet)
            {
                string part = (string)tag.Value;
                Check(new UTF8Encoding(false, true).GetByteCount(part) <= 250, "MTEXT chunk exceeds encoded-byte budget");
                Check(part.Length <= 250, "MTEXT chunk exceeds character budget");
            }
            document = loaded; binary = !binary;
        }
    }

    private static void MTextUnicodeChunkColumns(DxfVersion version, bool binary, string value)
    {
        var text = new MText(value) { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
        var doc = new DxfDocument(version);
        if (version < DxfVersion.AutoCad2018)
            doc.Entities.Add(text.ConvertToLinkedColumns(new[] { value.Substring(0, 249), value.Substring(249, 250), value.Substring(499) }));
        else doc.Entities.Add(text);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Column Unicode save failed"); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Column Unicode load failed");
        MText root = loaded.Entities.MTexts.Single(t => t.Columns != null);
        string actual = root.Columns!.Storage == MTextColumnStorage.LegacyLinked ? root.ConvertToEmbeddedColumns().Value : root.Value;
        Equal(value, actual, "Column Unicode text changed");
    }
}
