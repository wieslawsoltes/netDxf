using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string VPortApplication = "VPORT_TEST";
    private const string VPortConfigurationName = "Plan Żółć";

    private static void RegisterVPortWireTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"vport/wire/{v}/{b}/canonical", () => VPortWire(v, b, false));
                Run($"vport/wire/{v}/{b}/reordered", () => VPortWire(v, b, true));
                Run($"vport/defaults/{v}/{b}", () => VPortDefaults(v, b));
                Run($"vport/active-clone/{v}/{b}", () => VPortActiveClone(v, b));
                for (int failure = 0; failure < 14; failure++)
                {
                    int f = failure;
                    Run($"vport/invalid/{v}/{b}/{f}", () => VPortInvalid(v, b, f));
                }
                foreach (short first in new short[] { 10, 11, 12, 13, 14, 15, 16, 17, 110, 111, 112 })
                {
                    short c = first;
                    Run($"vport/incomplete-point/{v}/{b}/{c}", () => VPortIncompletePoint(v, b, c));
                }
            }
    }

    private static List<DxfTag> VPortPacket(int index)
    {
        return new List<DxfTag>
        {
            new(2, index < 2 ? (index == 0 ? "*ACTIVE" : "*aCtIvE") : "Plan \\U+017B\\U+00F3\\U+0142\\U+0107"),
            new(70, (short)64), new(10, index % 2 * 0.5), new(20, 0.0), new(11, (index % 2 + 1) * 0.5), new(21, 1.0),
            new(12, 7.25 + index), new(22, -3.5 - index), new(13, 0.125), new(23, -0.25),
            new(14, 1.25), new(24, 2.5), new(15, 5.0), new(25, 7.5),
            new(16, 2.0 + index), new(26, 3.0), new(36, 4.0), new(17, -5.0), new(27, 6.0), new(37, -7.0),
            new(40, 12.500000000000002 + index), new(41, 1.75), new(42, 85.5), new(43, -2.0), new(44, 500.0),
            new(50, 13.75), new(51, -42.5), new(71, (short)31), new(72, (short)1234), new(73, (short)0), new(74, (short)2),
            new(75, (short)1), new(76, (short)0), new(77, (short)1), new(78, (short)2), new(281, (short)6), new(65, (short)1),
            new(110, 1.0), new(120, 2.0), new(130, 3.0), new(111, 0.0), new(121, 1.0), new(131, 0.0),
            new(112, -1.0), new(122, 0.0), new(132, 0.0), new(79, (short)3), new(146, 5.25)
        };
    }

    private static List<DxfTag> VPortFixtureTags(DxfVersion version, bool reverse = false, int count = 4)
    {
        var tags = new List<DxfTag>
        {
            new(0,"SECTION"), new(2,"HEADER"), new(9,"$ACADVER"), new(1,HeaderVersion(version)),
            new(9,"$DWGCODEPAGE"), new(3,"ANSI_1252"), new(9,"$HANDSEED"), new(5,"1000"), new(0,"ENDSEC"),
            new(0,"SECTION"), new(2,"TABLES"), new(0,"TABLE"), new(2,"VPORT"), new(5,"A"), new(330,"0"),
            new(100,"AcDbSymbolTable"), new(70,(short)count), new(1001,VPortApplication), new(1000,"table metadata")
        };
        for (int i = 0; i < count; i++)
        {
            tags.AddRange(new DxfTag[] { new(0,"VPORT"), new(5,(0x100+i).ToString("X")), new(330,"A"),
                new(100,"AcDbSymbolTableRecord"), new(100,"AcDbViewportTableRecord") });
            var packet = VPortPacket(i);
            if (reverse) packet.Reverse();
            tags.AddRange(packet);
            tags.AddRange(new DxfTag[] { new(1001,VPortApplication), new(1000,"tile " + i), new(1004,new byte[] { (byte)i, 0, 255 }) });
        }
        tags.AddRange(new DxfTag[] { new(0,"ENDTAB"), new(0,"ENDSEC"), new(0,"SECTION"), new(2,"ENTITIES"),
            new(0,"LINE"), new(5,"200"), new(100,"AcDbEntity"), new(8,"0"), new(100,"AcDbLine"),
            new(10,20.0), new(20,30.0), new(30,40.0), new(11,50.0), new(21,60.0), new(31,70.0), new(0,"ENDSEC"), new(0,"EOF") });
        return tags;
    }

    private static void VPortWire(DxfVersion version, bool binary, bool reverse)
    {
        var tags = VPortFixtureTags(version, reverse);
        if (!binary && reverse)
        {
            int stop = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "ENDTAB"));
            for (int i = stop - 1; i > 20; i--) tags.Insert(i, new(999, "VPORT comment 0 ENDTAB"));
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("VPORT input rejected.");
        Check(input.CanRead, "VPORT reader closed caller stream.");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Near(7.25, doc.Viewport.ViewCenter.X, "First active viewport was discarded");
            SameDoubleBits(12.500000000000002, doc.Viewport.ViewHeight, "View height precision");
            Equal(new Vector3(2, 3, 4), doc.Viewport.ViewDirection, "View direction must retain magnitude");
            Equal("100", doc.Viewport.Handle, "First active handle");
            Equal("tile 0", (string)doc.Viewport.XData[VPortApplication].XDataRecord[0].Value, "Active record XData");
            Check(ReferenceEquals(doc.Viewport, doc.GetObjectByHandle("100")), "Active handle is not indexed.");
            Equal("table metadata", (string)doc.VPorts.XData[VPortApplication].XDataRecord[0].Value, "Table XData");
            foreach (int index in Enumerable.Range(0, 4))
            {
                var record = doc.GetObjectByHandle((0x100 + index).ToString("X")) as VPort;
                Check(record != null && ReferenceEquals(record.Owner, doc.VPorts), "VPORT record identity/owner lost.");
                Equal("tile " + index, (string)record!.XData[VPortApplication].XDataRecord[0].Value, "Repeated-name XData aliased");
            }
            Equal(VPortConfigurationName, ((VPort)doc.GetObjectByHandle("102")).Name, "Unicode configuration");
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle == 1 ? !binary : binary), "VPORT save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "VPORT").ToArray();
            Equal(4, records.Length, "Physical VPORT record count");
            for (int index = 0; index < records.Length; index++)
            {
                var packet = records[index].Tags.SkipWhile(t => !(t.Code == 100 && Equals(t.Value, "AcDbViewportTableRecord")))
                    .Skip(1).TakeWhile(t => t.Code != 1001).ToArray();
                foreach (DxfTag expected in VPortPacket(index).Where(t => t.Code != 2))
                {
                    var actual = packet.Single(t => t.Code == expected.Code);
                    if (expected.Value is double expectedDouble) SameDoubleBits(expectedDouble, (double)actual.Value, "Exact VPORT group " + expected.Code);
                    else Equal(expected.Value, actual.Value, "Exact VPORT group " + expected.Code);
                }
                Equal("A", (string)records[index].Tags.Single(t => t.Code == 330).Value, "Table owner edge");
            }
            if (cycle == 0 && !reverse)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"vport-{version}-{(binary ? "binary" : "text")}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("VPORT output failed to reload.");
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following entity consumed");
        }
    }

    private static void VPortDefaults(DxfVersion version, bool binary)
    {
        var tags = VPortFixtureTags(version, count: 0);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Empty VPORT table rejected.");
        Equal(1, doc.VPorts.Count, "Empty table default configuration");
        Equal(Vector3.UnitZ, doc.Viewport.ViewDirection, "Default direction");
        Check(ReferenceEquals(doc.GetObjectByHandle(doc.Viewport.Handle), doc.Viewport), "Default active identity missing");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Default VPORT failed to export");
        output.Position = 0; Check(DxfDocument.Load(output) != null, "Default VPORT failed to reload");
    }

    private static void VPortActiveClone(DxfVersion version, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(VPortFixtureTags(version), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("VPORT input rejected.");
        var clone = (VPort)doc.Viewport.Clone();
        Check(clone.SnapMode && !clone.ShowGrid, "Active viewport clone lost snap/grid state");
        Check(clone.Owner == null && clone.Handle == null && clone.IsReserved, "Active clone identity/flags");
        ((byte[])clone.XData[VPortApplication].XDataRecord[1].Value)[0] = 201;
        Equal((byte)0, ((byte[])doc.Viewport.XData[VPortApplication].XDataRecord[1].Value)[0], "Clone binary XData aliases source");
    }

    private static void VPortInvalid(DxfVersion version, bool binary, int scenario)
    {
        var tags = VPortFixtureTags(version);
        int start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbViewportTableRecord")) + 1;
        int end = tags.FindIndex(start, t => t.Code == 1001);
        void Set(short code, object value) { int at = tags.FindIndex(start, end-start, t => t.Code == code); tags[at] = new(code, value); }
        switch (scenario)
        {
            case 0: tags.RemoveAt(tags.FindIndex(start, t => t.Code == 2)); break;
            case 1: Set(2, "Invalid/Name"); break;
            case 2: tags.Insert(start, new(2, "Duplicate")); break;
            case 3: tags.Insert(start, new(40, 1.0)); break;
            case 4: Set(40, 0.0); break;
            case 5: Set(41, -1.0); break;
            case 6: Set(42, 0.0); break;
            case 7: Set(75, (short)2); break;
            case 8: Set(281, (short)7); break;
            case 9: Set(78, (short)3); break;
            case 10: Set(16, 0.0); Set(26, 0.0); Set(36, 0.0); break;
            case 11: Set(111, 0.0); Set(121, 0.0); Set(131, 0.0); break;
            case 12: Set(79, (short)7); break;
            case 13: Set(72, (short)0); break;
        }
        ExpectVPortInvalid(tags, binary);
    }

    private static void VPortIncompletePoint(DxfVersion version, bool binary, short first)
    {
        var tags = VPortFixtureTags(version);
        int start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbViewportTableRecord")) + 1;
        tags.RemoveAt(tags.FindIndex(start, t => t.Code == first));
        ExpectVPortInvalid(tags, binary);
    }

    private static void ExpectVPortInvalid(List<DxfTag> tags, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Malformed VPORT accepted.");
#endif
        Check(input.CanRead, "Invalid input closed caller stream.");
    }
}
