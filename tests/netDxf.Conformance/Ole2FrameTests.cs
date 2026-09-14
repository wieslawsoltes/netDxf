using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static byte[] OlePayload(int size) => Enumerable.Range(0, size).Select(i => (byte)(i * 131 % 256)).ToArray();
    private static void RegisterOle2FrameTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int size in new[] { 0, 1, 127, 128, 255, 1025 })
                {
                    int n = size;
                    Run($"ole2frame/wire/{v}/{b}/{n}", () => OleRoundTrip(v, b, n));
                }
                for (int failure = 0; failure < 19; failure++)
                {
                    int f = failure;
                    Run($"ole2frame/invalid/{v}/{b}/{f}", () => OleInvalid(v, b, f));
                }
                for (int placement = 0; placement < 4; placement++)
                {
                    int p = placement;
                    Run($"ole2frame/placement/{v}/{b}/{p}", () => OlePlacement(v, b, p));
                }
            }
        Run("ole2frame/api/isolation-validation-transform", OleApi);
    }

    private static List<DxfTag> OleTags(DxfVersion version, int size, short kind, bool reversed)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"), new(0, "SECTION"), new(2, "ENTITIES"),
            new(0, "OLE2FRAME"), new(5, "210"), new(100, "AcDbEntity"), new(8, "0"), new(62, (short)5), new(100, "AcDbOle2Frame")
        };
        var metadata = new List<DxfTag>
        {
            new(70, (short)2), new(3, "Picture \\U+017B\\U+00F3\\U+0142\\U+0107"),
            new(10, 1.0000000000000002), new(20, 6.0), new(30, -2.0),
            new(11, 8.0), new(21, -4.0), new(31, -2.0), new(71, kind), new(72, (short)0), new(90, size)
        };
        if (reversed) metadata.Reverse();
        tags.AddRange(metadata);
        byte[] data = OlePayload(size);
        // Noncanonical input chunk boundaries must not alter byte identity.
        for (int at = 0; at < data.Length; at += 31) tags.Add(new(310, data.Skip(at).Take(31).ToArray()));
        tags.AddRange(new DxfTag[]
        {
            new(1, "OLE"), new(1001, "OLE_TEST"), new(1000, "after binary data"),
            new(0, "LINE"), new(5, "211"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 10.0), new(20, 20.0), new(30, 30.0), new(11, 40.0), new(21, 50.0), new(31, 60.0),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void CheckOle(Ole2Frame frame, int size, short kind)
    {
        Equal(size, frame.BinaryDataLength, "OLE byte count");
        Check(frame.GetBinaryData().SequenceEqual(OlePayload(size)), "OLE bytes changed.");
        Equal("Picture Żółć", frame.Description, "OLE user-type description");
        Equal((short)2, frame.OleVersion, "OLE version"); Equal((OleObjectType)kind, frame.ObjectType, "OLE relationship");
        Equal((short)0, frame.TileMode, "Stored OLE tile mode");
        SameDoubleBits(1.0000000000000002, frame.UpperLeftCorner.X, "OLE corner precision");
        Equal(new Vector3(8, -4, -2), frame.LowerRightCorner, "OLE WCS lower corner");
        Equal((short)5, frame.Color.Index, "OLE common color");
        Equal("after binary data", (string)frame.XData["OLE_TEST"].XDataRecord.Single().Value, "Following OLE XData");
    }

    private static void OleRoundTrip(DxfVersion version, bool binary, int size)
    {
        foreach (short kind in new short[] { 1, 2, 3 })
        {
            var tags = OleTags(version, size, kind, true);
            if (!binary)
            {
                int end = tags.FindIndex(t => t.Code == 1001), start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbOle2Frame"));
                for (int i = end; i > start; i--) tags.Insert(i, new(999, "OLE2FRAME 310 1 OLE"));
            }
            using var input = new MemoryStream(RawFixtureBytes(tags, binary));
            var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("OLE fixture rejected.");
            Ole2Frame original = doc.Entities.Ole2Frames.Single(); CheckOle(original, size, kind);
            Equal("210", original.Handle, "Imported OLE handle");
            var copy = (Ole2Frame)original.Clone(); CheckOle(copy, size, kind);
            Check(copy.Handle == null && copy.Owner == null, "OLE clone kept identity.");
            doc.Entities.Add(copy);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                using var output = new MemoryStream(); bool format = cycle % 2 == 0 ? !binary : binary;
                Check(doc.Save(output, format), "OLE save failed.");
                output.Position = 0; var raw = DxfRawDocument.Load(output);
                foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "OLE2FRAME"))
                {
                    Equal(size, (int)record.Tags.Single(t => t.Code == 90).Value, "Serialized OLE count");
                    var chunks = record.Tags.Where(t => t.Code == 310).Select(t => (byte[])t.Value).ToArray();
                    Check(chunks.All(c => c.Length <= 127), "Oversized OLE chunk.");
                    Check(chunks.SelectMany(c => c).SequenceEqual(OlePayload(size)), "Serialized OLE bytes.");
                    Equal("OLE", (string)record.Tags.Single(t => t.Code == 1).Value, "OLE terminator");
                }
                if (cycle == 1 && size == 255 && kind == 2)
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"ole2frame-{version}-{binary}.dxf"), output.ToArray());
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("OLE reload failed.");
                Equal(2, doc.Entities.Ole2Frames.Count(), "OLE clone count");
                foreach (Ole2Frame frame in doc.Entities.Ole2Frames) CheckOle(frame, size, kind);
                Equal(new Vector3(10, 20, 30), doc.Entities.Lines.Single().StartPoint, "Entity after OLE lost.");
            }
            Check(input.CanRead, "OLE reader closed caller stream.");
        }
    }

    private static void OleInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = OleTags(version, 255, 2, false);
        int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbOle2Frame"));
        int at = tags.FindIndex(t => t.Code == 90), end = tags.FindIndex(t => t.Code == 1 && Equals(t.Value, "OLE"));
        switch (failure)
        {
            case 0: tags[at] = new(90, -1); break;
            case 1: tags[at] = new(90, 254); break;
            case 2: tags[at] = new(90, 256); break;
            case 3: tags[at] = new(90, int.MaxValue); break;
            case 4: tags.RemoveAt(at); break;
            case 5: tags.Insert(at, new(90, 255)); break;
            case 6: tags.RemoveAt(end); break;
            case 7: tags[end] = new(1, "NOT_OLE"); break;
            case 8: tags.Insert(end + 1, new(310, new byte[] { 1 })); break;
            case 9: tags.RemoveAt(tags.FindIndex(t => t.Code == 20)); break;
            case 10: break; // Inject nonfinite wire bytes below; DxfTag correctly forbids NaN.
            case 11: tags[tags.FindIndex(t => t.Code == 71)] = new(71, (short)4); break;
            case 12: tags[tags.FindIndex(t => t.Code == 72)] = new(72, (short)2); break;
            case 13: tags[tags.FindIndex(t => t.Code == 70)] = new(70, (short)-1); break;
            case 14: tags.Insert(marker + 1, new(100, "PrivateOle")); break;
            case 15: tags.Insert(marker + 1, new(102, "{PRIVATE")); break;
            case 16: tags[marker] = new(100, "AcDbWrongFrame"); break;
            case 17: tags[tags.FindIndex(marker, t => t.Code == 3)] = new(3, "A\\U+000AB"); break;
            case 18: tags.Insert(end, new(1001, "EARLY_XDATA")); break;
        }
        byte[] wire = RawFixtureBytes(tags, binary);
        if (failure == 10)
        {
            if (binary)
            {
                int coordinate = wire.AsSpan().IndexOf(BitConverter.GetBytes(1.0000000000000002));
                Check(coordinate >= 0, "Missing coordinate to corrupt.");
                Buffer.BlockCopy(BitConverter.GetBytes(double.NaN), 0, wire, coordinate, 8);
            }
            else
            {
                string text = System.Text.Encoding.UTF8.GetString(wire);
                Check(text.Contains("10\n1.0000000000000002\n", StringComparison.Ordinal), "Missing text coordinate.");
                wire = System.Text.Encoding.UTF8.GetBytes(text.Replace("10\n1.0000000000000002\n", "10\nNaN\n"));
            }
        }
        using var input = new MemoryStream(wire);
#if DEBUG
        if (failure == 10 && !binary) Throws<FormatException>(() => DxfDocument.Load(input));
        else Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Invalid OLE accepted.");
#endif
        Check(input.CanRead, "Invalid OLE closed caller stream.");
    }

    private static void OlePlacement(DxfVersion version, bool binary, int placement)
    {
        byte[] data = OlePayload(128); string description = "Type \\U+000A";
        var frame = new Ole2Frame(data, new Vector3(1, 2, 3), new Vector3(4, 5, 6), description, tileMode: (short)(placement == 1 ? 1 : 0));
        var doc = new DxfDocument(version);
        switch (placement)
        {
            case 0: doc.Entities.Add(frame); break;
            case 1: doc.Layouts.Add(new Layout("OlePaper")); doc.Entities.ActiveLayout = "OlePaper"; doc.Entities.Add(frame); doc.Entities.ActiveLayout = "Model"; break;
            case 2:
                var inner = new Block("OleInner"); inner.Entities.Add(frame);
                var outer = new Block("OleOuter"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
            default: var unused = new Block("OleUnused"); unused.Entities.Add(frame); doc.Blocks.Add(unused); break;
        }
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Placed OLE save failed.");
        stream.Position = 0; var restored = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Placed OLE reload failed.");
        var value = restored.Blocks.SelectMany(b => b.Entities).OfType<Ole2Frame>().Single();
        Check(value.GetBinaryData().SequenceEqual(data), "Placed OLE payload lost.");
        Equal(description, value.Description, "Literal OLE description"); Equal(frame.TileMode, value.TileMode, "Stored OLE mode");
        if (placement == 0) Check(restored.Entities.Remove(value), "OLE removal failed.");
    }

    private static void OleApi()
    {
        var bytes = OlePayload(256); var original = new Ole2Frame(bytes, Vector3.Zero, Vector3.UnitX);
        bytes[0] = 9; Equal((byte)0, original.GetBinaryData()[0], "Constructor aliased payload");
        var returned = original.GetBinaryData(); returned[1] = 0;
        Equal((byte)131, original.GetBinaryData()[1], "Getter aliased payload");
        var clone = (Ole2Frame)original.Clone(); var cloned = clone.GetBinaryData(); cloned[0] = 22;
        Equal((byte)0, original.GetBinaryData()[0], "Clone aliased payload");
        original.TransformBy(Matrix3.Identity, Vector3.Zero);
        foreach (var matrix in new[] { Matrix3.Scale(2), Matrix3.RotationZ(0.5), new Matrix3(double.NaN, 0, 0, 0, 1, 0, 0, 0, 1) })
        {
            Throws<NotSupportedException>(() => original.TransformBy(matrix, Vector3.Zero));
            Equal(Vector3.Zero, original.UpperLeftCorner, "Rejected transform mutated corner");
        }
        Throws<NotSupportedException>(() => original.TransformBy(Matrix3.Identity, new Vector3(1e-15, 0, 0)));
        Throws<ArgumentNullException>(() => new Ole2Frame(null!, Vector3.Zero, Vector3.Zero));
        Throws<ArgumentNullException>(() => new Ole2Frame(bytes, Vector3.Zero, Vector3.Zero, null!));
        foreach (string bad in new[] { "A\rB", "A\nB", "A\0B" }) Throws<ArgumentException>(() => new Ole2Frame(bytes, Vector3.Zero, Vector3.Zero, bad));
        Throws<ArgumentOutOfRangeException>(() => new Ole2Frame(bytes, new Vector3(double.PositiveInfinity, 0, 0), Vector3.Zero));
        var block = new Block("OleClone"); block.Entities.Add(original);
        var insert = new Insert(block); var copiedInsert = (Insert)insert.Clone();
        Check(!ReferenceEquals(original, copiedInsert.Block.Entities.OfType<Ole2Frame>().Single()), "Nested OLE clone alias.");
        Equal(1, insert.Explode().OfType<Ole2Frame>().Count(), "Identity OLE explosion");
        insert.Position = Vector3.UnitX;
        Throws<NotSupportedException>(() => insert.Explode());
        Equal(Vector3.Zero, original.UpperLeftCorner, "Failed INSERT explosion changed original OLE");
    }
}
