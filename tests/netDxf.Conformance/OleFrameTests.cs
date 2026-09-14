using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterOleFrameTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int size in new[] { 0, 1, 126, 127, 128, 1025 })
                {
                    int n = size;
                    Run($"oleframe/wire/{v}/{b}/{n}", () => LegacyOleRoundTrip(v, b, n));
                }
                for (int failure = 0; failure < 14; failure++)
                {
                    int f = failure;
                    Run($"oleframe/invalid/{v}/{b}/{f}", () => LegacyOleInvalid(v, b, f));
                }
                for (int placement = 0; placement < 4; placement++)
                {
                    int p = placement;
                    Run($"oleframe/placement/{v}/{b}/{p}", () => LegacyOlePlacement(v, b, p));
                }
            }
        Run("oleframe/api/isolation-transform", LegacyOleApi);
    }

    private static List<DxfTag> LegacyOleTags(DxfVersion version, int size, bool lateHeader)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"), new(0, "SECTION"), new(2, "ENTITIES"),
            new(0, "OLEFRAME"), new(5, "210"), new(100, "AcDbEntity"), new(8, "0"), new(62, (short)5), new(100, "AcDbOleFrame")
        };
        if (!lateHeader) { tags.Add(new(70, (short)1)); tags.Add(new(90, size)); }
        byte[] bytes = OlePayload(size);
        for (int at = 0; at < bytes.Length; at += 29) tags.Add(new(310, bytes.Skip(at).Take(29).ToArray()));
        if (lateHeader) { tags.Add(new(90, size)); tags.Add(new(70, (short)7)); }
        tags.AddRange(new DxfTag[]
        {
            new(1, "OLE"), new(1001, "LEGACY_OLE"), new(1000, "after legacy bytes"),
            new(0, "LINE"), new(5, "211"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 10.0), new(20, 20.0), new(30, 30.0), new(11, 40.0), new(21, 50.0), new(31, 60.0),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void LegacyOleRoundTrip(DxfVersion version, bool binary, int size)
    {
        foreach (bool late in new[] { false, true })
        {
            var tags = LegacyOleTags(version, size, late);
            if (!binary) tags.Insert(tags.FindIndex(t => t.Code == 90), new(999, "OLEFRAME 90 310 OLE"));
            using var input = new MemoryStream(RawFixtureBytes(tags, binary));
            var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Legacy OLE input rejected.");
            OleFrame original = doc.Entities.OleFrames.Single();
            Equal("210", original.Handle, "Legacy OLE input identity");
            var clone = (OleFrame)original.Clone();
            Check(clone.Handle == null && clone.Owner == null, "Legacy OLE clone retained identity.");
            doc.Entities.Add(clone);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                using var output = new MemoryStream();
                Check(doc.Save(output, cycle % 2 == 0 ? !binary : binary), "Legacy OLE save failed.");
                output.Position = 0;
                var raw = DxfRawDocument.Load(output);
                foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "OLEFRAME"))
                {
                    Equal(size, (int)record.Tags.Single(t => t.Code == 90).Value, "Legacy OLE wire byte count");
                    var chunks = record.Tags.Where(t => t.Code == 310).Select(t => (byte[])t.Value).ToArray();
                    Check(chunks.All(c => c.Length <= 127), "Oversized legacy OLE chunk.");
                    Check(chunks.SelectMany(c => c).SequenceEqual(OlePayload(size)), "Legacy OLE wire bytes changed.");
                    Equal("OLE", (string)record.Tags.Single(t => t.Code == 1).Value, "Legacy OLE terminator");
                }
                if (cycle == 1 && size == 1025 && late)
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"oleframe-{version}-{binary}.dxf"), output.ToArray());
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Legacy OLE reload failed.");
                Equal(2, doc.Entities.OleFrames.Count(), "Legacy OLE count");
                foreach (OleFrame frame in doc.Entities.OleFrames)
                {
                    Equal(size, frame.BinaryDataLength, "Legacy OLE length");
                    Check(frame.GetBinaryData().SequenceEqual(OlePayload(size)), "Legacy OLE bytes changed.");
                    Equal((short)(late ? 7 : 1), frame.OleVersion, "Legacy OLE version");
                    Equal((short)5, frame.Color.Index, "Legacy OLE color");
                    Equal("after legacy bytes", (string)frame.XData["LEGACY_OLE"].XDataRecord.Single().Value, "Legacy OLE XData");
                }
                Equal(new Vector3(10, 20, 30), doc.Entities.Lines.Single().StartPoint, "Legacy OLE following LINE");
            }
            Check(input.CanRead, "Legacy OLE closed input stream.");
        }
    }

    private static void LegacyOleInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = LegacyOleTags(version, 128, false);
        int size = tags.FindIndex(t => t.Code == 90), end = tags.FindIndex(t => t.Code == 1 && Equals(t.Value, "OLE"));
        switch (failure)
        {
            case 0: tags[size] = new(90, -1); break;
            case 1: tags[size] = new(90, 127); break;
            case 2: tags[size] = new(90, 129); break;
            case 3: tags[size] = new(90, int.MaxValue); break;
            case 4: tags.RemoveAt(size); break;
            case 5: tags.Insert(size, new(90, 128)); break;
            case 6: tags.RemoveAt(end); break;
            case 7: tags[end] = new(1, "NOT_OLE"); break;
            case 8: tags.Insert(end + 1, new(310, new byte[] { 1 })); break;
            case 9: tags.Insert(size, new(70, (short)1)); break;
            case 10: tags[tags.FindIndex(t => t.Code == 70)] = new(70, (short)-1); break;
            case 11: tags.Insert(end, new(100, "PrivateFrame")); break;
            case 12: tags.Insert(end, new(1001, "EARLY_XDATA")); break;
            case 13: tags[tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbOleFrame"))] = new(100, "AcDbOle2Frame"); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Invalid legacy OLE accepted.");
#endif
        Check(input.CanRead, "Invalid legacy OLE closed caller stream.");
    }

    private static void LegacyOlePlacement(DxfVersion version, bool binary, int placement)
    {
        var frame = new OleFrame(OlePayload(128)); var doc = new DxfDocument(version);
        switch (placement)
        {
            case 0: doc.Entities.Add(frame); break;
            case 1: doc.Layouts.Add(new Layout("LegacyPaper")); doc.Entities.ActiveLayout = "LegacyPaper"; doc.Entities.Add(frame); doc.Entities.ActiveLayout = "Model"; break;
            case 2:
                var inner = new Block("LegacyInner"); inner.Entities.Add(frame);
                var outer = new Block("LegacyOuter"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
            default: var unused = new Block("LegacyUnused"); unused.Entities.Add(frame); doc.Blocks.Add(unused); break;
        }
        doc.Entities.Add(new Ole2Frame(OlePayload(1), Vector3.Zero, Vector3.UnitX));
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Legacy OLE placement save failed.");
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Legacy OLE placement reload failed.");
        OleFrame found = loaded.Blocks.SelectMany(b => b.Entities).OfType<OleFrame>().Single();
        Check(found.GetBinaryData().SequenceEqual(OlePayload(128)), "Legacy placement bytes changed.");
        Equal(1, loaded.Entities.Ole2Frames.Count(), "Legacy OLE confused with OLE2FRAME.");
        if (placement == 0) Check(loaded.Entities.Remove(found), "Legacy OLE removal failed.");
    }

    private static void LegacyOleApi()
    {
        byte[] bytes = OlePayload(128); var frame = new OleFrame(bytes);
        bytes[0] = 23; Equal((byte)0, frame.GetBinaryData()[0], "Legacy constructor aliases bytes");
        byte[] copy = frame.GetBinaryData(); copy[0] = 42;
        Equal((byte)0, frame.GetBinaryData()[0], "Legacy getter aliases bytes");
        Throws<ArgumentNullException>(() => new OleFrame(null!));
        Throws<ArgumentOutOfRangeException>(() => new OleFrame(bytes, -1));
        var empty = new OleFrame(Array.Empty<byte>(), short.MaxValue); Equal(short.MaxValue, empty.OleVersion, "Version metadata normalized");
        frame.TransformBy(Matrix3.Identity, Vector3.Zero);
        Throws<NotSupportedException>(() => frame.TransformBy(Matrix3.Identity, new Vector3(1e-15, 0, 0)));
        Throws<NotSupportedException>(() => frame.TransformBy(Matrix3.Scale(2), Vector3.Zero));
        var block = new Block("LegacyInsert"); block.Entities.Add(frame); var insert = new Insert(block);
        var clone = (Insert)insert.Clone(); Check(!ReferenceEquals(frame, clone.Block.Entities.OfType<OleFrame>().Single()), "Legacy nested clone alias.");
        Equal(1, insert.Explode().OfType<OleFrame>().Count(), "Legacy identity explosion");
        insert.Position = Vector3.UnitX; Throws<NotSupportedException>(() => insert.Explode());
        Equal((byte)0, frame.GetBinaryData()[0], "Failed legacy transform mutated bytes");
        var tags = LegacyOleTags(DxfVersion.AutoCad2018, 0, false); tags.RemoveAll(t => t.Code == 70);
        using var stream = new MemoryStream(RawFixtureBytes(tags, false));
        Equal((short)1, DxfDocument.Load(stream)!.Entities.OleFrames.Single().OleVersion, "Legacy default version");
    }
}
