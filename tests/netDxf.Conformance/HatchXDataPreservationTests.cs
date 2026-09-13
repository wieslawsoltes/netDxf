using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchXDataPreservationTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                for (int scenario = 0; scenario < 5; scenario++)
                {
                    int s = scenario;
                    Run($"hatch/xdata/origin-context/{v}/{b}/{s}", () => HatchXDataRead(v, b, s));
                    Run($"hatch/xdata/preservation/{v}/{b}/{s}", () => HatchXDataSave(v, b, s));
                }
                Run($"hatch/xdata/absent-source/{v}/{b}", () => HatchXDataAbsent(v, b));
                Run($"hatch/xdata/failed-save/{v}/{b}", () => HatchXDataFailedSave(v, b));
                Run($"hatch/xdata/mixed-case-color-prefix/{v}/{b}", () => HatchXDataColorPrefix(v, b));
            }
    }

    private static XDataRecord[] OriginRecords(double x, double y) => new[]
    {
        new XDataRecord(XDataCode.RealX, x), new XDataRecord(XDataCode.RealY, y), new XDataRecord(XDataCode.RealZ, 0.0)
    };

    private static List<XDataRecord> HatchAcadRecords(int scenario, out int originAt)
    {
        var nested = new List<XDataRecord> { new(XDataCode.String, "NESTED"), XDataRecord.OpenControlString };
        nested.AddRange(OriginRecords(99, 88)); nested.Add(XDataRecord.CloseControlString);
        var records = new List<XDataRecord>(); originAt = -1;
        if (scenario == 0 || scenario == 3) { originAt = 0; records.AddRange(OriginRecords(7, 8)); }
        records.AddRange(nested);
        if (scenario == 1) { originAt = records.Count; records.AddRange(OriginRecords(7, 8)); }
        if (scenario == 3) records.AddRange(OriginRecords(55, 66));
        if (scenario == 4) records.Add(new(XDataCode.RealX, 123.0)); // An incomplete tuple is not the origin.
        records.Add(new(XDataCode.String, "unrelated tail"));
        records.Add(new(XDataCode.Int16, (short)17));
        records.Add(new(XDataCode.BinaryData, new byte[] { 0, 255, 128, 127 }));
        return records;
    }

    private static void SameXData(IReadOnlyList<XDataRecord> expected, IReadOnlyList<XDataRecord> actual)
    {
        Equal(expected.Count, actual.Count, "XData record count");
        for (int i = 0; i < expected.Count; i++)
        {
            Equal(expected[i].Code, actual[i].Code, "XData code/order");
            if (expected[i].Value is byte[] bytes) Check(bytes.SequenceEqual((byte[])actual[i].Value), "Opaque XData bytes changed.");
            else if (expected[i].Value is double real) SameDoubleBits(real, (double)actual[i].Value, "XData real bits");
            else Equal(expected[i].Value, actual[i].Value, "XData value");
        }
    }

    private static void HatchXDataRead(DxfVersion version, bool binary, int scenario)
    {
        var records = HatchAcadRecords(scenario, out int origin);
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 1);
        int end = tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        tags.InsertRange(end, new[] { new DxfTag(1001, "ACAD") }.Concat(records.Select(r => new DxfTag((short)r.Code, r.Value))));
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("ACAD XData input failed.");
        var hatch = doc.Entities.Hatches.Single();
        Equal(origin >= 0 ? new Vector2(7, 8) : Vector2.Zero, hatch.Pattern.Origin, "Nested/later/incomplete point overrode hatch origin");
        SameXData(records, hatch.XData["ACAD"].XDataRecord);
        Check(input.CanRead, "ACAD XData load closed input.");
    }

    private static void HatchXDataSave(DxfVersion version, bool binary, int scenario)
    {
        foreach (int fill in new[] { 0, 1, 2 })
        {
            Hatch hatch = (Hatch)NewSeedHatch().Clone();
            hatch.Pattern = fill == 0 ? HatchPattern.Line : fill == 1 ? HatchPattern.Solid :
                new HatchGradientPattern(new AciColor(1), new AciColor(5), HatchGradientPatternType.Linear);
            hatch.Pattern.Origin = new Vector2(21, 34);
            hatch.XData.Remove("ACAD");
            var originalRecords = HatchAcadRecords(scenario, out int origin);
            var acad = new XData(new ApplicationRegistry("ACAD")); acad.XDataRecord.AddRange(originalRecords); hatch.XData.Add(acad);
            foreach (string app in new[] { "GradientColor1ACI", "GradientColor2ACI" })
            {
                hatch.XData.Remove(app);
                var data = new XData(new ApplicationRegistry(app));
                data.XDataRecord.Add(new(XDataCode.Int16, (short)9));
                data.XDataRecord.Add(new(XDataCode.String, "gradient tail"));
                hatch.XData.Add(data);
            }
            var expected = new List<XDataRecord>(originalRecords);
            if (origin >= 0) { expected.RemoveRange(origin, 3); expected.InsertRange(origin, OriginRecords(21, 34)); }
            else expected.InsertRange(0, OriginRecords(21, 34));
            var doc = new DxfDocument(version); doc.Entities.Add(hatch);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                var snapshots = hatch.XData.Values.ToDictionary(d => d.ApplicationRegistry.Name, d => (XData)d.Clone());
                var identities = hatch.XData.Values.ToDictionary(d => d.ApplicationRegistry.Name, d => d.XDataRecord.ToArray());
                using var output = new MemoryStream(); Check(doc.Save(output, cycle == 1 ? !binary : binary), "XData preservation save failed.");
                Equal(snapshots.Count, hatch.XData.Count, "Save added source XData applications");
                foreach (var pair in snapshots)
                {
                    SameXData(pair.Value.XDataRecord, hatch.XData[pair.Key].XDataRecord);
                    Check(identities[pair.Key].SequenceEqual(hatch.XData[pair.Key].XDataRecord), "Save replaced source XData record objects.");
                }
                if (cycle == 0 && scenario == 0 && fill == 2)
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-xdata-{version}-{binary}.dxf"), output.ToArray());
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("XData preservation reload failed.");
                hatch = doc.Entities.Hatches.Single();
                SameXData(expected, hatch.XData["ACAD"].XDataRecord);
                Equal(new Vector2(21, 34), hatch.Pattern.Origin, "Origin did not survive nested metadata");
                foreach (string app in new[] { "GradientColor1ACI", "GradientColor2ACI" })
                {
                    var data = hatch.XData[app].XDataRecord;
                    Equal(2, data.Count, "Gradient XData suffix was discarded");
                    Equal("gradient tail", (string)data[1].Value, "Gradient suffix value changed");
                    short wanted = fill == 2 ? (short)(app.EndsWith("1ACI", StringComparison.Ordinal) ? 1 : 5) : (short)9;
                    Equal(wanted, (short)data[0].Value, "Gradient managed value incorrect");
                }
                Check(output.CanRead, "XData save closed caller output.");
            }
        }
    }

    private static void HatchXDataAbsent(DxfVersion version, bool binary)
    {
        Hatch hatch = (Hatch)NewSeedHatch().Clone(); hatch.XData.Clear();
        hatch.Pattern = new HatchGradientPattern(); hatch.Pattern.Origin = new Vector2(21, 34);
        var doc = new DxfDocument(version); doc.Entities.Add(hatch);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Absent XData save failed.");
        Equal(0, hatch.XData.Count, "Save injected managed data into the caller's hatch");
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Generated XData reload failed.");
        Equal(new Vector2(21, 34), loaded.Entities.Hatches.Single().Pattern.Origin, "Generated origin missing");
        Equal(3, loaded.Entities.Hatches.Single().XData.Count, "Managed output applications missing");
    }
    private static void HatchXDataFailedSave(DxfVersion version, bool binary)
    {
        Hatch hatch = (Hatch)NewSeedHatch().Clone(); hatch.XData.Remove("ACAD");
        var data = new XData(new ApplicationRegistry("ACAD")); data.XDataRecord.AddRange(HatchAcadRecords(0, out _));
        hatch.XData.Add(data); hatch.Pattern.Origin = new Vector2(21, 34);
        var doc = new DxfDocument(version); doc.Entities.Add(hatch);
        using var probe = new MemoryStream(); Check(doc.Save(probe, binary), "Failure test output probe failed.");
        var identity = data.XDataRecord.ToArray(); var snapshot = (XData)data.Clone();
        using var output = new HatchLateFailStream(probe.Length - 1);
#if DEBUG
        Throws<IOException>(() => doc.Save(output, binary));
#else
        Check(!doc.Save(output, binary), "Injected output failure was not reported.");
#endif
        SameXData(snapshot.XDataRecord, data.XDataRecord);
        Check(identity.SequenceEqual(data.XDataRecord), "Failed output replaced caller XData records.");
        Check(output.CanWrite, "Failed save closed caller output.");
    }

    private static void HatchXDataColorPrefix(DxfVersion version, bool binary)
    {
        Hatch hatch = (Hatch)NewSeedHatch().Clone(); hatch.XData.Clear();
        hatch.Pattern = new HatchGradientPattern(new AciColor(1), new AciColor(5), HatchGradientPatternType.Linear);
        foreach (string app in new[] { "acad", "gradientcolor1aci", "gradientcolor2aci" })
        {
            var data = new XData(new ApplicationRegistry(app));
            data.XDataRecord.Add(new(XDataCode.String, "prefix")); hatch.XData.Add(data);
        }
        var doc = new DxfDocument(version); doc.Entities.Add(hatch);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Mixed-case XData save failed.");
        foreach (XData data in hatch.XData.Values) Equal(1, data.XDataRecord.Count, "Projection inserted source records");
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Mixed-case XData load failed.");
        var restored = loaded.Entities.Hatches.Single();
        Equal(3, restored.XData.Count, "Case-insensitive APPID duplicated");
        Equal(4, restored.XData["ACAD"].XDataRecord.Count, "ACAD prefix not preserved");
        Equal("prefix", (string)restored.XData["ACAD"].XDataRecord[3].Value, "ACAD prefix overwritten");
        foreach (string app in new[] { "GradientColor1ACI", "GradientColor2ACI" })
        {
            Equal(2, restored.XData[app].XDataRecord.Count, "Color prefix not preserved");
            Equal("prefix", (string)restored.XData[app].XDataRecord[1].Value, "Color prefix overwritten");
        }
    }

    private sealed class HatchLateFailStream(long limit) : MemoryStream
    {
        private void Guard(int count) { if (Position + count > limit) throw new IOException("Injected late HATCH save failure."); }
        public override void Write(byte[] buffer, int offset, int count) { Guard(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Guard(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Guard(1); base.WriteByte(value); }
    }

}
