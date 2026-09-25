// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Qualification
{
    internal static class Polyline2DEditCases
    {
        internal sealed class Case
        {
            internal readonly string Id;
            internal readonly Action Test;
            internal Case(string id, Action test) { Id = id; Test = test; }
        }
        private static readonly DxfVersion[] Versions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
            DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
        private static readonly string[] Operations = { "none", "position", "zero", "bulge", "widths", "clear",
            "closed", "linetype", "elevation", "thickness", "same-position", "same-bulge", "same-widths", "same-header" };
        private static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        private static double NegativeZero { get { return BitConverter.Int64BitsToDouble(long.MinValue); } }
        private static byte[]? Cache(int kind) { return kind == 0 ? null : kind == 1 ? Array.Empty<byte>() : new byte[] { 80, 50, 69, 0, 255 }; }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static bool Equal(double? a, double? b)
        { return a.HasValue == b.HasValue && (!a.HasValue || Bits(a.Value) == Bits(b!.Value)); }
        private static void CacheEquals(byte[]? expected, byte[]? actual)
        { Check(expected == null ? actual == null : actual != null && expected.SequenceEqual(actual), "Parent proxy state changed unexpectedly"); }
        private static void Refuses(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "Invalid edit accepted");
        }
        private static Polyline2D NewPolyline()
        {
            var vertices = new[] { new Polyline2DVertex(1, 2), new Polyline2DVertex(4, 6),
                new Polyline2DVertex(-2, 7), new Polyline2DVertex(11, -3) };
            double[] bulges = { .25, -.5, 0, 0 };
            double?[] starts = { null, .75, 1, null }, ends = { .5, null, 1.5, null };
            for (int i = 0; i < vertices.Length; i++)
            { vertices[i].Bulge = bulges[i]; vertices[i].StartWidthOverride = starts[i]; vertices[i].EndWidthOverride = ends[i]; vertices[i].VertexIdentifier = 101 + i; }
            return new Polyline2D(vertices) { Elevation = 3, Thickness = .5 };
        }
        private static byte[] Save(DxfDocument document, bool binary)
        { using (var stream = new MemoryStream()) { Check(document.Save(stream, binary) && stream.CanWrite, "Save/stream lifetime"); return stream.ToArray(); } }
        private static DxfDocument Load(byte[] bytes)
        {
            var before = (byte[])bytes.Clone();
            using (var stream = new MemoryStream(bytes))
            {
                var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Typed load failed");
                Check(stream.CanRead && before.SequenceEqual(bytes), "Source bytes or stream changed"); return document;
            }
        }
        // Independent legacy input: replace a valid lightweight seed packet with an explicitly
        // specified POLYLINE/VERTEX/SEQEND chain. No new typed edit method authors these values.
        private static byte[] LegacyInput(DxfDocument seed, Polyline2D source)
        {
            // The lightweight seed must obey its existing 2013+ identifier gate. Legacy
            // VERTEX IDs are injected independently below, using the original model values.
            var identifiers = source.Vertexes.Select(v => v.VertexIdentifier).ToArray();
            byte[] seedBytes;
            try
            {
                if (seed.DrawingVariables.AcadVer < DxfVersion.AutoCad2013)
                    foreach (var vertex in source.Vertexes) vertex.VertexIdentifier = null;
                seedBytes = Save(seed, false);
            }
            finally { for (int i = 0; i < identifiers.Length; i++) source.Vertexes[i].VertexIdentifier = identifiers[i]; }
            string[] text = Encoding.UTF8.GetString(seedBytes).Replace("\r\n", "\n").Split('\n');
            var tags = new List<KeyValuePair<int, string>>(); ulong maximum = 0;
            for (int i = 0; i + 1 < text.Length; i += 2)
            {
                int code = int.Parse(text[i], CultureInfo.InvariantCulture); tags.Add(new KeyValuePair<int, string>(code, text[i + 1]));
                if (code == 5 || code == 105) maximum = Math.Max(maximum, ulong.Parse(text[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            int first = tags.FindIndex(t => t.Key == 0 && t.Value == "LWPOLYLINE");
            int last = tags.FindIndex(first + 1, t => t.Key == 0);
            int geometry = tags.FindIndex(first, t => t.Key == 100 && t.Value == "AcDbPolyline");
            var packet = new List<KeyValuePair<int, string>>();
            Action<int, string> add = (code, value) => packet.Add(new KeyValuePair<int, string>(code, value));
            Func<double, string> number = value => value.ToString("R", CultureInfo.InvariantCulture);
            add(0, "POLYLINE"); packet.AddRange(tags.Skip(first + 1).Take(geometry - first - 1));
            add(100, "AcDb2dPolyline"); add(10, "0"); add(20, "0"); add(30, "3"); add(39, ".5"); add(70, "0");
            add(40, "2"); add(41, "3"); add(75, "0"); add(210, "0"); add(220, "0"); add(230, "1");
            for (int i = 0; i < source.Vertexes.Count; i++)
            {
                var vertex = source.Vertexes[i]; add(0, "VERTEX"); add(5, (++maximum).ToString("X", CultureInfo.InvariantCulture));
                add(330, i % 2 == 0 ? source.Handle : source.Owner.Record.Handle);
                add(100, "AcDbEntity"); add(8, "0"); add(100, "AcDbVertex"); add(100, "AcDb2dVertex");
                add(10, number(vertex.Position.X)); add(20, number(vertex.Position.Y)); add(30, "0"); add(70, "0");
                if (vertex.StartWidthOverride.HasValue) add(40, number(vertex.StartWidthOverride.Value));
                if (vertex.EndWidthOverride.HasValue) add(41, number(vertex.EndWidthOverride.Value));
                add(42, number(vertex.Bulge)); add(91, vertex.VertexIdentifier!.Value.ToString(CultureInfo.InvariantCulture));
                add(1001, "P2_RECORD"); add(1000, "vertex-" + i);
            }
            add(0, "SEQEND"); add(5, (++maximum).ToString("X", CultureInfo.InvariantCulture)); add(330, source.Handle);
            add(100, "AcDbEntity"); add(8, "0"); add(1001, "P2_RECORD"); add(1000, "terminator");
            tags.RemoveRange(first, last - first); tags.InsertRange(first, packet);
            int handseed = tags.FindIndex(t => t.Key == 9 && t.Value == "$HANDSEED");
            tags[handseed + 1] = new KeyValuePair<int, string>(5, (++maximum).ToString("X", CultureInfo.InvariantCulture));
            var output = new StringBuilder(); foreach (var tag in tags) { output.Append(tag.Key.ToString(CultureInfo.InvariantCulture)).Append('\n'); output.Append(tag.Value).Append('\n'); }
            return Encoding.UTF8.GetBytes(output.ToString());
        }
        private static Polyline2D Subject(int mode, out DxfDocument document, DxfVersion version = DxfVersion.AutoCad2018)
        {
            document = new DxfDocument(version); document.ApplicationRegistries.Add(new ApplicationRegistry("P2_RECORD"));
            var subject = NewPolyline();
            if (mode != 2 && version < DxfVersion.AutoCad2013) foreach (var vertex in subject.Vertexes) vertex.VertexIdentifier = null;
            document.Entities.Add(subject); string handle = subject.Handle;
            if (mode > 0) { document = Load(mode == 2 ? LegacyInput(document, subject) : Save(document, false)); subject = (Polyline2D)document.GetObjectByHandle(handle); }
            Check((subject.EndSequenceRecord != null) == (mode == 2), "Incorrect subject representation"); return subject;
        }
        private static string VertexState(Polyline2DVertex? vertex)
        {
            if (vertex == null) return "null";
            return string.Join(",", Bits(vertex.Position.X), Bits(vertex.Position.Y), Bits(vertex.Bulge),
                vertex.StartWidthOverride.HasValue ? Bits(vertex.StartWidthOverride.Value).ToString(CultureInfo.InvariantCulture) : "absent",
                vertex.EndWidthOverride.HasValue ? Bits(vertex.EndWidthOverride.Value).ToString(CultureInfo.InvariantCulture) : "absent", vertex.VertexIdentifier);
        }
        private static string State(Polyline2D item)
        {
            return string.Join(";", item.Vertexes.Select(VertexState)) + "|" + Bits(item.Elevation) + "|" + Bits(item.Thickness)
                + "|" + item.IsClosed + "|" + item.LinetypeGeneration + "|" + item.SmoothType + "|" + item.CodeName
                + "|" + string.Join(",", item.VertexRecords.Select(r => r.Handle)) + "|" + item.EndSequenceRecord?.Handle;
        }
        internal static IEnumerable<Case> All(string? directory)
        {
            for (int mode = 0; mode < 3; mode++) for (int cache = 0; cache < 3; cache++)
            {
                int m = mode, c = cache;
                for (int property = 0; property < 5; property++)
                { int p = property; yield return new Case($"header/change/{m}/{c}/{p}", () => Header(m, c, p, false)); yield return new Case($"header/noop/{m}/{c}/{p}", () => Header(m, c, p, true)); }
                for (int operation = 0; operation < 9; operation++) { int op = operation; yield return new Case($"vertex/change/{m}/{c}/{op}", () => VertexEdit(m, c, op)); }
                for (int operation = 0; operation < 3; operation++) { int op = operation; yield return new Case($"vertex/noop/{m}/{c}/{op}", () => Noop(m, c, op)); }
                for (int fault = 0; fault < 19; fault++) { int f = fault; yield return new Case($"refused/{m}/{c}/{f}", () => Refused(m, c, f)); }
            }
            for (int cache = 0; cache < 3; cache++)
            {
                int c = cache;
                yield return new Case("header/scalar-bits/" + c, () => HeaderBits(c));
                yield return new Case("packet-budget/" + c, () => PacketBudget(c));
                yield return new Case("clone-and-masked-widths/" + c, () => CloneAndWidths(c));
                yield return new Case("retained-state/" + c, () => RetainedState(c));
                yield return new Case("identity-without-callbacks/" + c, () => IdentityAdmission(c));
            }
            foreach (var version in Versions) foreach (bool binary in new[] { false, true }) foreach (bool legacy in new[] { false, true })
                yield return new Case($"wire/{version}/{binary}/{legacy}", () => Wire(version, binary, legacy, directory));
        }
        internal static void VerifyInstalled()
        { foreach (var item in All(null)) try { item.Test(); } catch (Exception error) { throw new InvalidOperationException("Installed Polyline2D edit: " + item.Id, error); } }
        private static void Header(int mode, int cache, int property, bool noop)
        {
            var item = Subject(mode, out var document); item.ProxyGraphics = Cache(cache);
            string before = State(item); var vertices = item.Vertexes.ToArray(); var records = item.VertexRecords.ToArray(); var end = item.EndSequenceRecord;
            Action apply = () =>
            {
                switch (property)
                {
                    case 0: item.Elevation = noop ? item.Elevation : 9; break;
                    case 1: item.Thickness = noop ? item.Thickness : -2; break;
                    case 2: item.IsClosed = noop ? item.IsClosed : true; break;
                    case 3: item.LinetypeGeneration = noop ? item.LinetypeGeneration : true; break;
                    case 4: item.SmoothType = noop ? item.SmoothType : PolylineSmoothType.Quadratic; break;
                }
            };
            bool refused = !noop && mode == 2 && property == 4;
            if (refused) Refuses(apply); else apply();
            if (noop || refused) Check(before == State(item), "Header no-op/refusal changed state");
            else Check(before != State(item), "Header change had no effect");
            CacheEquals(noop || refused ? Cache(cache) : null, item.ProxyGraphics);
            Check(vertices.SequenceEqual(item.Vertexes) && records.SequenceEqual(item.VertexRecords) && ReferenceEquals(end, item.EndSequenceRecord), "Header edit replaced child identities");
            item.ProxyGraphics = Cache(cache); before = State(item); Refuses(() => item.SmoothType = PolylineSmoothType.BezierSurface);
            Check(before == State(item), "Refused smoothing changed state"); CacheEquals(Cache(cache), item.ProxyGraphics);
        }
        private static void VertexEdit(int mode, int cache, int operation)
        {
            var item = Subject(mode, out var document); var expected = NewPolyline();
            var target = item.Vertexes[1]; var wanted = expected.Vertexes[1]; var list = item.Vertexes; var vertices = list.ToArray(); var records = item.VertexRecords.ToArray(); var end = item.EndSequenceRecord;
            if (operation == 3) { target.Bulge = 0; wanted.Bulge = 0; }
            item.ProxyGraphics = Cache(cache); var enumerator = list.GetEnumerator(); Check(enumerator.MoveNext(), "Enumerator setup");
            switch (operation)
            {
                case 0: wanted.Position = new Vector2(20, 30); item.SetVertex(1, wanted.Position); break;
                case 1: wanted.Position = new Vector2(NegativeZero, 6); item.SetVertex(1, wanted.Position); break;
                case 2: wanted.Bulge = .75; item.SetVertexBulge(1, wanted.Bulge); break;
                case 3: wanted.Bulge = NegativeZero; item.SetVertexBulge(1, wanted.Bulge); break;
                case 4: wanted.StartWidthOverride = 2; wanted.EndWidthOverride = 4; item.SetVertexWidths(1, 2, 4); break;
                case 5: wanted.StartWidthOverride = null; wanted.EndWidthOverride = null; item.SetVertexWidths(1, null, null); break;
                case 6: wanted.StartWidthOverride = 0; wanted.EndWidthOverride = 0; item.SetVertexWidths(1, 0, 0); break;
                case 7: wanted.StartWidthOverride = NegativeZero; wanted.EndWidthOverride = NegativeZero; item.SetVertexWidths(1, NegativeZero, NegativeZero); break;
                case 8: wanted.EndWidthOverride = .25; item.SetVertexWidths(1, wanted.StartWidthOverride, wanted.EndWidthOverride); break;
            }
            Check(expected.Vertexes.Select(VertexState).SequenceEqual(item.Vertexes.Select(VertexState)), "Wrong edited or unselected vertex fields");
            CacheEquals(null, item.ProxyGraphics);
            Check(ReferenceEquals(list, item.Vertexes) && vertices.SequenceEqual(item.Vertexes) && records.SequenceEqual(item.VertexRecords)
                && ReferenceEquals(end, item.EndSequenceRecord) && ReferenceEquals(document.GetObjectByHandle(item.Handle), item), "Vertex edit replaced graph identities");
            Check(enumerator.MoveNext(), "Vertex edit changed list enumeration version"); enumerator.Dispose();
            foreach (var record in item.VertexRecords) Check(ReferenceEquals(record.Owner, item) && ReferenceEquals(document.GetObjectByHandle(record.Handle), record), "Record registration changed");
            Check(document.Objects.Validate().Count == 0, "Edited graph is invalid");
        }
        private static void Noop(int mode, int cache, int operation)
        {
            var item = Subject(mode, out var document); item.ProxyGraphics = Cache(cache); string before = State(item);
            var iterator = item.Vertexes.GetEnumerator(); Check(iterator.MoveNext(), "No-op enumerator setup"); var vertex = item.Vertexes[0];
            if (operation == 0) item.SetVertex(0, vertex.Position);
            if (operation == 1) item.SetVertexBulge(0, vertex.Bulge);
            if (operation == 2) item.SetVertexWidths(0, vertex.StartWidthOverride, vertex.EndWidthOverride);
            Check(before == State(item) && iterator.MoveNext(), "No-op changed state or enumeration"); iterator.Dispose(); CacheEquals(Cache(cache), item.ProxyGraphics);
        }
        private static void Refused(int mode, int cache, int fault)
        {
            var item = Subject(mode, out var document);
            if (fault == 14) item.Vertexes[3] = null!;
            if (fault == 15) item.Vertexes[3].Position = new Vector2(double.NaN, 0);
            if (fault == 16) item.Vertexes[3].Bulge = double.PositiveInfinity;
            if (fault == 17) item.Vertexes[3] = item.Vertexes[0];
            if (fault == 18) item.Vertexes[3].Position = new Vector2(0, double.NegativeInfinity);
            item.ProxyGraphics = Cache(cache); string before = State(item); var vertices = item.Vertexes.ToArray();
            Refuses(() =>
            {
                switch (fault)
                {
                    case 0: item.SetVertex(-1, Vector2.Zero); break;
                    case 1: item.SetVertex(4, Vector2.Zero); break;
                    case 2: item.SetVertex(0, new Vector2(double.NaN, 0)); break;
                    case 3: item.SetVertex(0, new Vector2(0, double.PositiveInfinity)); break;
                    case 4: item.SetVertexBulge(0, double.NaN); break;
                    case 5: item.SetVertexBulge(0, double.NegativeInfinity); break;
                    case 6: item.SetVertexWidths(0, 2, -1); break;
                    case 7: item.SetVertexWidths(0, 2, double.PositiveInfinity); break;
                    case 8: item.SetVertexWidths(0, double.NaN, null); break;
                    case 9: item.SetVertexWidths(0, -1, 2); break;
                    case 10: item.SetVertexWidths(-1, null, null); break;
                    case 11: item.SetVertexWidths(4, null, null); break;
                    case 12: item.SetVertexBulge(-1, 0); break;
                    case 13: item.SetVertexBulge(4, 0); break;
                    default: item.SetVertex(0, item.Vertexes[0].Position); break;
                }
            });
            Check(before == State(item) && vertices.SequenceEqual(item.Vertexes), "Rejected edit partially mutated a vertex"); CacheEquals(Cache(cache), item.ProxyGraphics);
        }
        private static void HeaderBits(int cache)
        {
            double[] values = { 0, NegativeZero, double.Epsilon, double.MaxValue, -2,
                BitConverter.Int64BitsToDouble(Bits(1.0) + 1), double.PositiveInfinity, double.NegativeInfinity,
                BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001UL)), BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000002UL)) };
            foreach (double value in values) foreach (bool thickness in new[] { false, true })
            {
                var item = NewPolyline(); item.ProxyGraphics = Cache(cache);
                if (thickness) item.Thickness = value; else item.Elevation = value;
                Check(Bits(thickness ? item.Thickness : item.Elevation) == Bits(value), "Header setter changed candidate bits/admission"); CacheEquals(null, item.ProxyGraphics);
                item.ProxyGraphics = Cache(cache); if (thickness) item.Thickness = value; else item.Elevation = value;
                CacheEquals(Cache(cache), item.ProxyGraphics);
            }
        }
        private static void PacketBudget(int cache)
        {
            foreach (int limit in new[] { 4095, 4096 })
            {
                var item = Subject(2, out var document); var record = item.VertexRecords[0];
                var count = typeof(Polyline2DRecord).GetMethod("TopologyTagCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var data = record.XData["P2_RECORD"];
                while ((int)count.Invoke(record, null)! < limit) data.XDataRecord.Add(new XDataRecord(XDataCode.String, "budget"));
                item.ProxyGraphics = Cache(cache); string before = State(item);
                if (limit == 4096)
                { Refuses(() => item.SetVertexWidths(0, 1, .5)); Check(before == State(item), "Over-budget edit partially changed widths"); CacheEquals(Cache(cache), item.ProxyGraphics); }
                else { item.SetVertexWidths(0, 1, .5); Check((int)count.Invoke(record, null)! == 4096, "Exact packet budget not admitted"); CacheEquals(null, item.ProxyGraphics); }
                item.SetVertex(0, new Vector2(20, 30)); Check(Bits(item.Vertexes[0].Position.X) == Bits(20), "Count-preserving edit at packet limit failed");
            }
        }
        private static void CloneAndWidths(int cache)
        {
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var source = Subject(mode, out var document); source.ProxyGraphics = Cache(cache); var clone = (Polyline2D)source.Clone();
                var target = new DxfDocument(DxfVersion.AutoCad2018); target.Entities.Add(clone);
                string before = State(source); clone.SetVertex(0, new Vector2(20, 30)); clone.SetVertexBulge(1, .75); clone.SetVertexWidths(2, null, 0);
                Check(State(source) == before && !source.Vertexes.Intersect(clone.Vertexes).Any(), "Clone edit changed source or aliased vertices");
                CacheEquals(Cache(cache), source.ProxyGraphics); CacheEquals(null, clone.ProxyGraphics);
                Check(clone.GetEffectiveStartWidth(2) == (mode == 2 ? 2 : 0) && clone.GetEffectiveEndWidth(2) == 0, "Absent/zero width inheritance");
                if (mode != 2) { clone.ConstantWidth = 5; clone.SetVertexWidths(2, 2, 3); Check(clone.ConstantWidth == 5 && clone.GetEffectiveStartWidth(2) == 5, "Raw edit incorrectly removed constant width"); }
            }
        }
        private static void RetainedState(int cache)
        {
            foreach (bool reorder in new[] { false, true })
            {
                var item = Subject(2, out var document);
                if (reorder) item.Vertexes.Reverse(); else item.Vertexes.Add(new Polyline2DVertex(12, 13));
                item.ProxyGraphics = Cache(cache); string before = State(item); Refuses(() => item.SetVertexWidths(0, 1, 2));
                Check(before == State(item), "Invalid retained mapping changed"); CacheEquals(Cache(cache), item.ProxyGraphics);
            }
        }
        private sealed class HostileVertex : Polyline2DVertex
        {
            internal HostileVertex() : base(1, 2) { }
            public override bool Equals(object? obj) { throw new InvalidOperationException("User Equals called"); }
            public override int GetHashCode() { throw new InvalidOperationException("User hash called"); }
        }
        private static void IdentityAdmission(int cache)
        {
            var item = new Polyline2D(new[] { new HostileVertex(), new HostileVertex() }); item.ProxyGraphics = Cache(cache);
            item.SetVertex(0, new Vector2(3, 4)); item.SetVertexBulge(1, .25); item.SetVertexWidths(1, 1, 2); CacheEquals(null, item.ProxyGraphics);
        }
        private static void Apply(Polyline2D item, string operation)
        {
            switch (operation)
            {
                case "position": item.SetVertex(1, new Vector2(20, 30)); break;
                case "zero": item.SetVertex(1, new Vector2(NegativeZero, 6)); break;
                case "bulge": item.SetVertexBulge(1, .75); break;
                case "widths": item.SetVertexWidths(1, 2, 4); break;
                case "clear": item.SetVertexWidths(1, null, null); break;
                case "closed": item.IsClosed = true; break;
                case "linetype": item.LinetypeGeneration = true; break;
                case "elevation": item.Elevation = 9; break;
                case "thickness": item.Thickness = -2; break;
                case "same-position": item.SetVertex(1, item.Vertexes[1].Position); break;
                case "same-bulge": item.SetVertexBulge(1, item.Vertexes[1].Bulge); break;
                case "same-widths": item.SetVertexWidths(1, item.Vertexes[1].StartWidthOverride, item.Vertexes[1].EndWidthOverride); break;
                case "same-header": item.Elevation = item.Elevation; item.Thickness = item.Thickness; item.IsClosed = item.IsClosed; item.LinetypeGeneration = item.LinetypeGeneration; break;
            }
        }
        private static bool Changed(string operation) { return operation != "none" && !operation.StartsWith("same-", StringComparison.Ordinal); }
        private static void CheckWireGeometry(Polyline2D item, string operation, bool identifiers)
        {
            var expected = NewPolyline();
            if (!identifiers) foreach (var vertex in expected.Vertexes) vertex.VertexIdentifier = null;
            switch (operation)
            {
                case "position": expected.Vertexes[1].Position = new Vector2(20, 30); break;
                case "zero": expected.Vertexes[1].Position = new Vector2(NegativeZero, 6); break;
                case "bulge": expected.Vertexes[1].Bulge = .75; break;
                case "widths": expected.Vertexes[1].StartWidthOverride = 2; expected.Vertexes[1].EndWidthOverride = 4; break;
                case "clear": expected.Vertexes[1].StartWidthOverride = null; expected.Vertexes[1].EndWidthOverride = null; break;
            }
            Check(expected.Vertexes.Select(VertexState).SequenceEqual(item.Vertexes.Select(VertexState)), "Wire vertex values/presence/identifiers");
            Check(Bits(item.Elevation) == Bits(operation == "elevation" ? 9 : 3) && Bits(item.Thickness) == Bits(operation == "thickness" ? -2 : .5)
                && item.IsClosed == (operation == "closed") && item.LinetypeGeneration == (operation == "linetype"), "Wire header fields");
        }
        private static void Wire(DxfVersion version, bool binary, bool legacy, string? directory)
        {
            var template = Subject(legacy ? 2 : 0, out var unused, version);
            var seed = new DxfDocument(version); var subjects = new List<Polyline2D>();
            for (int place = 0; place < 4; place++)
            {
                var group = new List<Polyline2D>();
                for (int cache = 0; cache < 3; cache++) foreach (string operation in Operations)
                {
                    var item = (Polyline2D)template.Clone(); var data = new XData(new ApplicationRegistry("P2_EDITS"));
                    data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"P2_{place}_{cache}_{operation}")); item.XData.Add(data); item.ProxyGraphics = Cache(cache); group.Add(item);
                }
                if (place == 0) foreach (var item in group) seed.Entities.Add(item);
                else if (place == 1) { var layout = new Layout("P2_PAPER"); seed.Layouts.Add(layout); foreach (var item in group) layout.AssociatedBlock.Entities.Add(item); }
                else { var block = new Block("P2_CONTAINER_" + place); foreach (var item in group) block.Entities.Add(item); if (place == 2) seed.Entities.Add(new Insert(block)); else seed.Blocks.Add(block); }
                subjects.AddRange(group);
            }
            var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(following);
            string prefix = "polyline2d-edits-" + version + "-" + (binary ? "binary" : "text") + "-" + (legacy ? "legacy" : "lightweight");
            byte[] source = Save(seed, binary); if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source);
            var document = Load(source); var identities = new Dictionary<string, string[]>();
            foreach (var original in subjects)
            {
                var item = (Polyline2D)document.GetObjectByHandle(original.Handle); string[] parts = ((string)item.XData["P2_EDITS"].XDataRecord.Single().Value).Split('_');
                int cache = int.Parse(parts[2], CultureInfo.InvariantCulture); string operation = parts[3]; CacheEquals(Cache(cache), item.ProxyGraphics);
                var vertices = item.Vertexes.ToArray(); var records = item.VertexRecords.ToArray(); var end = item.EndSequenceRecord;
                Apply(item, operation); CheckWireGeometry(item, operation, legacy || version >= DxfVersion.AutoCad2013); CacheEquals(Changed(operation) ? null : Cache(cache), item.ProxyGraphics);
                Check(vertices.SequenceEqual(item.Vertexes) && records.SequenceEqual(item.VertexRecords) && ReferenceEquals(end, item.EndSequenceRecord), "Wire edit replaced identities");
                identities.Add(item.Handle, item.VertexRecords.Select(r => r.Handle).Concat(legacy ? new[] { item.EndSequenceRecord.Handle } : Array.Empty<string>()).ToArray());
            }
            for (int stage = 0; stage < 2; stage++)
            {
                byte[] bytes = Save(document, stage == 0 ? binary : !binary); if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
                document = Load(bytes);
                foreach (var pair in identities)
                {
                    var item = (Polyline2D)document.GetObjectByHandle(pair.Key); string[] parts = ((string)item.XData["P2_EDITS"].XDataRecord.Single().Value).Split('_');
                    int cache = int.Parse(parts[2], CultureInfo.InvariantCulture); string operation = parts[3]; CheckWireGeometry(item, operation, legacy || version >= DxfVersion.AutoCad2013); CacheEquals(Changed(operation) ? null : Cache(cache), item.ProxyGraphics);
                    Check(pair.Value.SequenceEqual(item.VertexRecords.Select(r => r.Handle).Concat(legacy ? new[] { item.EndSequenceRecord.Handle } : Array.Empty<string>())), "Cross-save vertex or terminator identity changed");
                    foreach (var record in item.VertexRecords) Check(ReferenceEquals(record.Owner, item) && ReferenceEquals(document.GetObjectByHandle(record.Handle), record), "Wire record registration/owner");
                }
                var line = (Line)document.GetObjectByHandle(following.Handle); Check(line.StartPoint == following.StartPoint && line.EndPoint == following.EndPoint, "Following line geometry changed");
                Check(document.Objects.Validate().Count == 0, "Wire object graph validation");
            }
        }
    }
}
