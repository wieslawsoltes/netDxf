using System.IO.Compression;
using System.Security.Cryptography;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string CommonProxyNativeFile = "sample_AC1024_ascii.dxf";
    private const string CommonProxyNativeSha256 = "c97e857047ad4cecd84754ea1a8638e47508e339fd489f1e895ee7332127b372";

    private static void RegisterCommonProxy160Tests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            foreach (int size in new[] { 0, 1, 127, 128, 129, 1025 })
            {
                int n = size;
                Run($"common-data/proxy160/wire/{v}/{b}/{n}", () => CommonProxy160Wire(v, b, n));
            }
            for (int fault = 0; fault < 6; fault++)
            {
                int f = fault;
                Run($"common-data/proxy160/malformed/{v}/{b}/{f}", () => CommonProxy160Malformed(v, b, f));
            }
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"common-data/proxy160/native-packets/{b}", () => CommonProxy160Native(b));
        }
    }

    private static List<DxfTag> CommonProxy160Tags(DxfVersion version, int size)
    {
        var tags = CommonDataTags(version, size);
        tags[tags.FindIndex(t => t.Code is 92 or 160)] = new(160, (long)size);
        return tags;
    }

    private static void CommonProxy160Wire(DxfVersion version, bool binary, int size)
    {
        foreach (bool reordered in new[] { false, true })
        {
            var tags = CommonProxy160Tags(version, size);
            if (reordered)
            {
                int at = tags.FindIndex(t => t.Code == 160); var count = tags[at]; tags.RemoveAt(at);
                tags.Insert(tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbOleFrame"), count);
            }
            if (version < DxfVersion.AutoCad2010) { CommonDataReject(tags, binary); continue; }
            using var input = new MemoryStream(RawFixtureBytes(tags, binary));
            var document = DxfDocument.Load(input) ?? throw new Exception("Valid group 160 packet rejected");
            var frame = document.Entities.OleFrames.Single(); AssertCommonData(frame, version, size);
            var clone = (OleFrame)frame.Clone(); AssertCommonData(clone, version, size);
            clone.ProxyGraphics = new byte[] { 255 };
            AssertCommonData(frame, version, size);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                using var output = new MemoryStream(); Check(document.Save(output, cycle == 0 ? binary : !binary), "Group 160 save failed");
                output.Position = 0;
                var record = DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Single(r => r.Name == "OLEFRAME");
                var common = CommonSubclass(record); var count = common.Single(t => t.Code is 92 or 160);
                Equal(version < DxfVersion.AutoCad2013 ? (short)92 : (short)160, count.Code, "Canonical output count code");
                Equal((long)size, Convert.ToInt64(count.Value), "Canonical output count value");
                Check(common.Where(t => t.Code == 310).All(t => ((byte[])t.Value).Length <= 127), "Canonical output chunk size");
                output.Position = 0; document = DxfDocument.Load(output) ?? throw new Exception("Group 160 reload failed");
                frame = document.Entities.OleFrames.Single(); AssertCommonData(frame, version, size);
                Check(frame.GetBinaryData().SequenceEqual(OlePayload(17)), "Proxy packet consumed native OLE bytes");
                Equal("after legacy bytes", (string)frame.XData["LEGACY_OLE"].XDataRecord.Single().Value, "Following XData changed");
                Equal(new Vector3(10, 20, 30), document.Entities.Lines.Single().StartPoint, "Following entity changed");
            }
        }
    }

    private static void CommonProxy160Malformed(DxfVersion version, bool binary, int fault)
    {
        var variants = new List<List<DxfTag>>();
        void Length(long value)
        {
            var tags = CommonProxy160Tags(version, 129); tags[tags.FindIndex(t => t.Code == 160)] = new(160, value); variants.Add(tags);
        }
        switch (fault)
        {
            case 0: Length(-1); Length(long.MinValue); break;
            case 1: Length(long.MaxValue); Length(4294967425L); Length(int.MaxValue); break;
            case 2: Length(128); Length(130); break;
            case 3:
                foreach (short duplicate in new short[] { 92, 160 })
                {
                    var tags = CommonProxy160Tags(version, 129); int at = tags.FindIndex(t => t.Code == 160);
                    tags.Insert(at, duplicate == 92 ? new DxfTag(92, 129) : new DxfTag(160, 129L)); variants.Add(tags);
                }
                break;
            case 4:
                Length(EntityObject.MaximumProxyGraphicsBytes + 1L);
                var missing = CommonProxy160Tags(version, 129); int index = missing.FindIndex(t => t.Code == 160);
                missing.RemoveRange(index + 1, missing.FindIndex(index + 1, t => t.Code == 100) - index - 1);
                missing[index] = new(160, (long)EntityObject.MaximumProxyGraphicsBytes);
                long before = GC.GetAllocatedBytesForCurrentThread(); CommonDataReject(missing, binary);
                Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "Missing payload allocated its advertised 16 MiB");
                break;
            case 5:
                var oversized = CommonProxy160Tags(version, 129); oversized[oversized.FindIndex(t => t.Code == 310)] = new(310, CommonDataBytes(129)); variants.Add(oversized);
                var noCount = CommonProxy160Tags(version, 129); noCount.RemoveAt(noCount.FindIndex(t => t.Code == 160)); variants.Add(noCount);
                break;
        }
        foreach (var tags in variants) CommonDataReject(tags, binary);
    }

    private static void CommonProxy160Native(bool binary)
    {
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", CommonProxyNativeFile + ".gz"));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var original = new MemoryStream(); gzip.CopyTo(original);
        Equal(CommonProxyNativeSha256, Convert.ToHexString(SHA256.HashData(original.ToArray())).ToLowerInvariant(), "Native proxy source hash");
        original.Position = 0; var source = DxfRawDocument.Load(original);
        Equal(DxfVersion.AutoCad2010, source.Version, "Native proxy source profile");
        var packets = source.Sections.SelectMany(s => s.Records).Where(r => CommonSubclass(r).Any(t => t.Code == 160)).ToArray();
        Equal(22, packets.Length, "Pinned native proxy packet inventory");
        Equal(45416L, packets.Sum(r => Convert.ToInt64(CommonSubclass(r).Single(t => t.Code == 160).Value)), "Pinned native proxy byte inventory");
        // Reuse exact native common packet tags in inert LINE carriers. This does
        // not qualify the original entity bodies or their referenced resources.
        var setup = new DxfDocument(DxfVersion.AutoCad2010);
        foreach (var packet in packets)
        {
            string handle = (string)packet.Tags.Single(t => t.Code == 5).Value;
            byte[] bytes = CommonSubclass(packet).Where(t => t.Code == 310).SelectMany(t => (byte[])t.Value).ToArray();
            var line = new Line(new Vector3(setup.Entities.Lines.Count(), 0, 0), Vector3.UnitY);
            var xdata = new XData(new ApplicationRegistry("NATIVE_PROXY_SOURCE"));
            foreach (string value in new[] { CommonProxyNativeFile, handle, packet.Name, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() })
                xdata.XDataRecord.Add(new XDataRecord(XDataCode.String, value));
            line.XData.Add(xdata); setup.Entities.Add(line);
        }
        using var saved = new MemoryStream(); Check(setup.Save(saved, binary), "Native carrier setup failed"); saved.Position = 0;
        var raw = DxfRawDocument.Load(saved);
        for (int i = 0; i < packets.Length; i++)
        {
            var carrier = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Where(r => r.Name == "LINE").ElementAt(i);
            var tags = carrier.Tags.ToList(); int boundary = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbLine");
            tags.InsertRange(boundary, CommonSubclass(packets[i]).Where(t => t.Code is 160 or 310));
            raw = raw.WithRecord(carrier, tags);
        }
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        var loaded = DxfDocument.Load(input) ?? throw new Exception("Native R2010 proxy carriers rejected");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            Equal(22, loaded.Entities.Lines.Count(), "Native carrier count");
            foreach (var line in loaded.Entities.Lines)
            {
                string handle = (string)line.XData["NATIVE_PROXY_SOURCE"].XDataRecord[1].Value;
                var packet = packets.Single(r => (string)r.Tags.Single(t => t.Code == 5).Value == handle);
                byte[] bytes = CommonSubclass(packet).Where(t => t.Code == 310).SelectMany(t => (byte[])t.Value).ToArray();
                Check(line.ProxyGraphics!.SequenceEqual(bytes), "Native cache bytes changed");
                var clone = (Line)line.Clone(); Check(clone.ProxyGraphics!.SequenceEqual(bytes), "Native clone bytes changed");
                clone.ClearProxyGraphics(); Check(line.ProxyGraphics!.SequenceEqual(bytes), "Native clone shares cache state");
            }
            using var output = new MemoryStream(); Check(loaded.Save(output, cycle == 0 ? binary : !binary), "Native carrier save failed");
            if (cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"common-data-native160-AutoCad2010-{binary}.dxf"), output.ToArray());
            output.Position = 0; loaded = DxfDocument.Load(output) ?? throw new Exception("Native carrier reload failed");
        }
    }
}
