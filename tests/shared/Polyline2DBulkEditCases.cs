// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Qualification
{
    internal static partial class Polyline2DEditCases
    {
        private static readonly MethodInfo PacketCounter = typeof(Polyline2DRecord)
            .GetMethod("TopologyTagCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static int PacketCount(Polyline2DRecord record) { return (int)PacketCounter.Invoke(record, null)!; }
        private static void FillPacket(Polyline2DRecord record, int count)
        {
            int missing = count - PacketCount(record);
            Check(missing >= 0, "Invalid budget fixture");
            var data = record.XData["P2_RECORD"];
            for (int i = 0; i < missing; i++) data.XDataRecord.Add(new XDataRecord(XDataCode.String, "budget"));
            Check(PacketCount(record) == count, "Budget fixture count differs");
        }
        private static string CompleteBulkState(Polyline2D item)
        {
            return State(item) + "|" + item.LegacyDefaultStartWidth + "|" + item.LegacyDefaultEndWidth
                + "|" + string.Join(",", item.VertexRecords.Select(PacketCount));
        }
        private static void BulkApply(Polyline2D item, bool reverse)
        { if (reverse) item.Reverse(); else item.SetConstantWidth(5); }
        private static IEnumerable<Case> BulkCases(string? directory)
        {
            for (int cache = 0; cache < 3; cache++) foreach (bool reverse in new[] { false, true })
            {
                int c = cache; bool r = reverse;
                foreach (int size in new[] { 4094, 4095, 4096 })
                { int n = size; yield return new Case($"bulk/packet/{c}/{r}/{n}", () => BulkPacket(c, r, n)); }
            }
            for (int cache = 0; cache < 3; cache++)
            { int c = cache; yield return new Case("bulk/exact-noop/" + c, () => BulkNoop(c)); }
            foreach (bool overflow in new[] { false, true })
                yield return new Case("bulk/aggregate/" + overflow, () => BulkAggregate(overflow));
            foreach (var version in Versions) foreach (bool binary in new[] { false, true }) foreach (bool reverse in new[] { false, true })
                yield return new Case($"bulk/wire/{version}/{binary}/{reverse}", () => BulkWire(version, binary, reverse, directory));
        }
        private static void BulkPacket(int cache, bool reverse, int size)
        {
            var item = Subject(2, out var document);
            var record = item.VertexRecords[3]; FillPacket(record, size);
            item.ProxyGraphics = Cache(cache); string before = CompleteBulkState(item);
            var list = item.Vertexes; var vertices = list.ToArray(); var records = item.VertexRecords.ToArray();
            var end = item.EndSequenceRecord; var iterator = list.GetEnumerator(); Check(iterator.MoveNext(), "Enumerator setup");
            if (size > 4094)
            {
                Refuses(() => BulkApply(item, reverse));
                Check(before == CompleteBulkState(item), "Bulk refusal partially changed coordinates, widths, ordering or defaults");
                Check(iterator.MoveNext(), "Bulk refusal changed the list version");
                CacheEquals(Cache(cache), item.ProxyGraphics);
                Check(vertices.SequenceEqual(item.Vertexes) && records.SequenceEqual(item.VertexRecords), "Bulk refusal changed identities");
            }
            else
            {
                BulkApply(item, reverse); Check(PacketCount(record) == 4096, "Exact packet boundary not admitted");
                CacheEquals(null, item.ProxyGraphics);
                Check((reverse ? vertices.Reverse() : vertices).SequenceEqual(item.Vertexes), "Bulk operation replaced vertex identity");
                Check((reverse ? records.Reverse() : records).SequenceEqual(item.VertexRecords), "Bulk operation replaced retained identity");
                Check(document.Objects.Validate().Count == 0, "Successful boundary edit left invalid graph");
            }
            Check(ReferenceEquals(list, item.Vertexes) && ReferenceEquals(end, item.EndSequenceRecord), "Bulk operation replaced list or terminator");
            iterator.Dispose();
        }
        private static void BulkNoop(int cache)
        {
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var item = Subject(mode, out var document); item.SetConstantWidth(5);
                if (mode == 2) FillPacket(item.VertexRecords[3], 4096);
                item.ProxyGraphics = Cache(cache); string before = CompleteBulkState(item);
                var iterator = item.Vertexes.GetEnumerator(); Check(iterator.MoveNext(), "No-op enumerator setup");
                item.SetConstantWidth(5); Check(before == CompleteBulkState(item) && iterator.MoveNext(), "No-op changed state/version");
                CacheEquals(Cache(cache), item.ProxyGraphics); iterator.Dispose();
                foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity })
                {
                    Refuses(() => item.SetConstantWidth(invalid));
                    Check(before == CompleteBulkState(item), "Invalid width partially changed bulk state"); CacheEquals(Cache(cache), item.ProxyGraphics);
                }
            }
        }
        private static void BulkAggregate(bool overflow)
        {
            const int vertexCount = 256, maximum = 1048576;
            var seed = new DxfDocument(DxfVersion.AutoCad2018); seed.ApplicationRegistries.Add(new ApplicationRegistry("P2_RECORD"));
            var source = new Polyline2D(Enumerable.Range(0, vertexCount).Select(i =>
                new Polyline2DVertex(i * 2, i % 3) { VertexIdentifier = 101 + i }));
            seed.Entities.Add(source);
            var document = Load(LegacyInput(seed, source));
            var item = (Polyline2D)document.GetObjectByHandle(source.Handle);
            int target = maximum - 2 * vertexCount + (overflow ? 1 : 0);
            for (int i = 0; i < vertexCount - 1; i++) FillPacket(item.VertexRecords[i], 4094);
            int last = target - PacketCount(item.EndSequenceRecord) - (vertexCount - 1) * 4094;
            FillPacket(item.VertexRecords[vertexCount - 1], last);
            long beforeCount = item.VertexRecords.Sum(r => (long)PacketCount(r)) + PacketCount(item.EndSequenceRecord);
            Check(beforeCount == target && item.VertexRecords.All(r => PacketCount(r) <= 4094), "Aggregate fixture differs");
            string before = CompleteBulkState(item); item.ProxyGraphics = Cache(2);
            if (overflow)
            {
                Refuses(() => item.SetConstantWidth(5));
                Check(before == CompleteBulkState(item), "Aggregate refusal changed a live width"); CacheEquals(Cache(2), item.ProxyGraphics);
            }
            else
            {
                item.SetConstantWidth(5);
                Check(item.VertexRecords.Sum(r => (long)PacketCount(r)) + PacketCount(item.EndSequenceRecord) == maximum, "Exact aggregate budget not admitted");
                CacheEquals(null, item.ProxyGraphics);
            }
        }
        private static void BulkWire(DxfVersion version, bool binary, bool reverse, string? directory)
        {
            var item = Subject(2, out var document, version);
            FillPacket(item.VertexRecords[3], 4094); item.ProxyGraphics = Cache(2);
            var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); document.Entities.Add(following);
            string handle = item.Handle, endHandle = item.EndSequenceRecord.Handle;
            var records = item.VertexRecords.ToArray(); var recordIds = records.Select(r => r.Handle).ToArray();
            var sourceVertices = item.Vertexes.Select(v => (Polyline2DVertex)v.Clone()).ToArray();
            string prefix = $"polyline2d-bulk-{version}-{(binary ? "binary" : "text")}-{(reverse ? "reverse" : "width")}";
            byte[] original = Save(document, binary);
            if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), original);
            BulkApply(item, reverse);
            for (int stage = 0; stage < 2; stage++)
            {
                byte[] bytes = Save(document, stage == 0 ? binary : !binary);
                if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
                document = Load(bytes); item = (Polyline2D)document.GetObjectByHandle(handle);
                Check((reverse ? recordIds.Reverse() : recordIds).SequenceEqual(item.VertexRecords.Select(r => r.Handle))
                    && item.EndSequenceRecord.Handle == endHandle, "Bulk save changed retained identities");
                CacheEquals(null, item.ProxyGraphics);
                Check(Equal(item.LegacyDefaultStartWidth, reverse ? 3 : 2) && Equal(item.LegacyDefaultEndWidth, reverse ? 2 : 3), "Inherited defaults lost orientation");
                for (int i = 0; i < 4; i++)
                {
                    int old = reverse ? 3 - i : i, segment = (old + 3) % 4;
                    var vertex = item.Vertexes[i]; var position = sourceVertices[old];
                    Check(Bits(vertex.Position.X) == Bits(position.Position.X) && Bits(vertex.Position.Y) == Bits(position.Position.Y)
                        && vertex.VertexIdentifier == position.VertexIdentifier, "Bulk edit changed position/identifier");
                    Check(Bits(vertex.Bulge) == Bits(reverse ? -sourceVertices[segment].Bulge : sourceVertices[old].Bulge), "Bulk edit changed wrong bulge");
                    Check(Equal(vertex.StartWidthOverride, reverse ? sourceVertices[segment].EndWidthOverride : 5)
                        && Equal(vertex.EndWidthOverride, reverse ? sourceVertices[segment].StartWidthOverride : 5), "Bulk widths/presence differ");
                    Check(ReferenceEquals(item.VertexRecords[i].Owner, item)
                        && ReferenceEquals(document.GetObjectByHandle(item.VertexRecords[i].Handle), item.VertexRecords[i]), "Bulk record registration");
                }
                Check(PacketCount(item.VertexRecords[reverse ? 0 : 3]) == 4096, "Serialized boundary packet count");
                var line = (Line)document.GetObjectByHandle(following.Handle);
                Check(line.StartPoint == following.StartPoint && line.EndPoint == following.EndPoint, "Following LINE changed");
                Check(document.Objects.Validate().Count == 0, "Bulk wire graph");
            }
        }
    }
}
