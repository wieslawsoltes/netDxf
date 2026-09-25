// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Qualification
{
    internal static class Polyline3DEditCases
    {
        internal sealed class Case
        {
            internal readonly string Id;
            internal readonly Action Test;
            internal Case(string id, Action test) { Id = id; Test = test; }
        }
        private static readonly DxfVersion[] Versions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
            DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
        private static readonly string[] Operations = { "none", "set", "zero", "insert", "remove", "move",
            "reverse", "closed", "linetype", "same-index", "same-set" };
        private static Vector3[] Points() { return new[] { new Vector3(1, 2, 3), new Vector3(4, 6, 8),
            new Vector3(-2, 7, 5), new Vector3(11, -3, 9) }; }
        private static byte[]? Cache(int kind) { return kind == 0 ? null : kind == 1 ? Array.Empty<byte>() : new byte[] { 80, 51, 71, 0, 255 }; }
        private static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        private static bool Same(Vector3 a, Vector3 b) { return Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void SamePoints(IEnumerable<Vector3> a, IEnumerable<Vector3> b)
        { var x = a.ToArray(); var y = b.ToArray(); Check(x.Length == y.Length && x.Zip(y, Same).All(value => value), "Exact coordinate sequence changed"); }
        private static void SameCache(byte[]? expected, byte[]? actual)
        { Check(expected == null ? actual == null : actual != null && expected.SequenceEqual(actual), "Absent/empty/nonempty parent proxy cache differs"); }
        private static byte[] Save(DxfDocument document, bool binary)
        { using (var stream = new MemoryStream()) { Check(document.Save(stream, binary) && stream.CanWrite, "Save/stream lifetime"); return stream.ToArray(); } }
        private static DxfDocument Load(byte[] bytes)
        {
            var before = (byte[])bytes.Clone();
            using (var stream = new MemoryStream(bytes))
            {
                var result = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Typed load failed");
                Check(stream.CanRead && before.SequenceEqual(bytes), "Load changed source bytes/stream lifetime"); return result;
            }
        }
        private static Polyline3D Subject(bool retained, out DxfDocument document)
        {
            document = new DxfDocument(DxfVersion.AutoCad2018);
            var item = new Polyline3D(Points());
            var data = new XData(new ApplicationRegistry("P3_TAG")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); item.XData.Add(data);
            document.Entities.Add(item);
            if (retained) { string handle = item.Handle; document = Load(Save(document, false)); item = (Polyline3D)document.GetObjectByHandle(handle); }
            Check((item.EndSequenceRecord != null) == retained, "Wrong retained test setup"); return item;
        }
        private static string State(Polyline3D item)
        {
            return string.Join(";", item.Vertexes.Select(p => Bits(p.X) + "," + Bits(p.Y) + "," + Bits(p.Z)))
                + "|" + item.IsClosed + "|" + item.LinetypeGeneration + "|" + item.SmoothType
                + "|" + string.Join(",", item.VertexRecords.Select(r => r.Handle)) + "|" + item.EndSequenceRecord?.Handle;
        }
        private static void Refuses(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "Invalid edit was accepted");
        }
        internal static IEnumerable<Case> All(string? directory)
        {
            foreach (bool retained in new[] { false, true }) for (int cache = 0; cache < 3; cache++)
            {
                bool stored = retained; int kind = cache;
                for (int operation = 0; operation < 8; operation++) { int op = operation; yield return new Case($"edit/{retained}/{cache}/{operation}", () => Edit(stored, kind, op)); }
                for (int operation = 0; operation < 5; operation++) { int op = operation; yield return new Case($"noop/{retained}/{cache}/{operation}", () => Noop(stored, kind, op)); }
                for (int fault = 0; fault < 12; fault++) { int mode = fault; yield return new Case($"refused/{retained}/{cache}/{fault}", () => Refused(stored, kind, mode)); }
            }
            for (int cache = 0; cache < 3; cache++)
            {
                int kind = cache;
                yield return new Case("empty-singleton/" + cache, () => EmptySingleton(kind));
                yield return new Case("scalar-bits/" + cache, () => ScalarBits(kind));
                yield return new Case("clone/" + cache, () => Clone(kind));
                yield return new Case("referenced-removal/" + cache, () => ReferencedRemoval(kind));
                yield return new Case("retained-state/" + cache, () => RetainedState(kind));
            }
            foreach (var version in Versions) foreach (bool binary in new[] { false, true })
                yield return new Case($"wire/{version}/{binary}", () => Wire(version, binary, directory));
        }
        internal static void VerifyInstalled()
        { foreach (var item in All(null)) try { item.Test(); } catch (Exception error) { throw new InvalidOperationException("Installed Polyline3D edit: " + item.Id, error); } }

        private static void Edit(bool retained, int cache, int operation)
        {
            var item = Subject(retained, out var document); item.ProxyGraphics = Cache(cache);
            var list = item.Vertexes; var owner = item.Owner; var layer = item.Layer; var data = item.XData["P3_TAG"];
            var records = item.VertexRecords.ToArray(); var end = item.EndSequenceRecord;
            string handle = item.Handle;
            var expected = Points().ToList();
            switch (operation)
            {
                case 0: item.SetVertex(1, new Vector3(20, 30, 40)); expected[1] = new Vector3(20, 30, 40); break;
                case 1: item.InsertVertex(1, new Vector3(20, 30, 40)); expected.Insert(1, new Vector3(20, 30, 40)); break;
                case 2: item.RemoveVertexAt(1); expected.RemoveAt(1); break;
                case 3: item.MoveVertex(0, 2); expected = new[] { Points()[1], Points()[2], Points()[0], Points()[3] }.ToList(); break;
                case 4: item.Reverse(); expected.Reverse(); break;
                case 5: item.IsClosed = true; break;
                case 6: item.LinetypeGeneration = true; break;
                case 7: item.SmoothType = PolylineSmoothType.Quadratic; break;
            }
            SamePoints(expected, item.Vertexes); SameCache(null, item.ProxyGraphics);
            Check(ReferenceEquals(list, item.Vertexes) && ReferenceEquals(owner, item.Owner) && ReferenceEquals(layer, item.Layer)
                && ReferenceEquals(data, item.XData["P3_TAG"]) && handle == item.Handle && ReferenceEquals(document.GetObjectByHandle(handle), item), "Edit replaced parent/common identities");
            Check(item.IsClosed == (operation == 5) && item.LinetypeGeneration == (operation == 6)
                && item.SmoothType == (operation == 7 ? PolylineSmoothType.Quadratic : PolylineSmoothType.NoSmooth), "Edit changed wrong flags");
            if (!retained) return;
            Check(ReferenceEquals(end, item.EndSequenceRecord), "Terminator identity changed");
            var wanted = records.ToList();
            if (operation == 1)
            {
                var added = item.VertexRecords[1]; Check(!records.Contains(added) && ReferenceEquals(added.Owner, item)
                    && ReferenceEquals(document.GetObjectByHandle(added.Handle), added), "Inserted identity not registered"); wanted.Insert(1, added);
            }
            if (operation == 2)
            {
                Check(records[1].IsRemoved && records[1].Owner == null && document.GetObjectByHandle(records[1].Handle) == null, "Removed identity not retired"); wanted.RemoveAt(1);
            }
            if (operation == 3) wanted = new[] { records[1], records[2], records[0], records[3] }.ToList();
            if (operation == 4) wanted.Reverse();
            Check(wanted.SequenceEqual(item.VertexRecords), "Retained slot identities changed");
        }
        private static void Noop(bool retained, int cache, int operation)
        {
            var item = Subject(retained, out var document); item.ProxyGraphics = Cache(cache);
            string before = State(item); var list = item.Vertexes; var records = item.VertexRecords.ToArray();
            switch (operation)
            {
                case 0: item.SetVertex(1, item.Vertexes[1]); break;
                case 1: item.MoveVertex(1, 1); break;
                case 2: item.IsClosed = item.IsClosed; break;
                case 3: item.LinetypeGeneration = item.LinetypeGeneration; break;
                case 4: item.SmoothType = item.SmoothType; break;
            }
            Check(State(item) == before && ReferenceEquals(list, item.Vertexes) && records.SequenceEqual(item.VertexRecords), "No-op changed state/identities");
            SameCache(Cache(cache), item.ProxyGraphics); Check(document.Objects.Validate().Count == 0, "No-op graph");
        }
        private static void Refused(bool retained, int cache, int fault)
        {
            var item = Subject(retained, out var document); item.ProxyGraphics = Cache(cache);
            if (fault == 11) item.Vertexes[3] = new Vector3(double.NaN, 0, 0);
            string before = State(item); var records = item.VertexRecords.ToArray();
            Refuses(() =>
            {
                switch (fault)
                {
                    case 0: item.SetVertex(-1, Vector3.Zero); break;
                    case 1: item.SetVertex(4, Vector3.Zero); break;
                    case 2: item.SetVertex(0, new Vector3(double.NaN, 0, 0)); break;
                    case 3: item.SetVertex(0, new Vector3(0, double.PositiveInfinity, 0)); break;
                    case 4: item.SetVertex(0, new Vector3(0, 0, double.NegativeInfinity)); break;
                    case 5: item.InsertVertex(-1, Vector3.Zero); break;
                    case 6: item.InsertVertex(1, new Vector3(double.NaN, 0, 0)); break;
                    case 7: item.RemoveVertexAt(4); break;
                    case 8: item.MoveVertex(-1, 0); break;
                    case 9: item.MoveVertex(0, 4); break;
                    case 10: item.SmoothType = PolylineSmoothType.BezierSurface; break;
                    case 11: item.SetVertex(0, Vector3.Zero); break;
                }
            });
            Check(before == State(item) && records.SequenceEqual(item.VertexRecords), "Refused edit changed state/identities"); SameCache(Cache(cache), item.ProxyGraphics);
        }
        private static void EmptySingleton(int cache)
        {
            foreach (int count in new[] { 0, 1 })
            {
                var item = new Polyline3D(Points().Take(count)); item.ProxyGraphics = Cache(cache);
                item.Reverse(); SameCache(Cache(cache), item.ProxyGraphics);
                if (count == 1) { item.MoveVertex(0, 0); item.SetVertex(0, item.Vertexes[0]); SameCache(Cache(cache), item.ProxyGraphics); }
                item.InsertVertex(count, Vector3.UnitZ); SameCache(null, item.ProxyGraphics);
                item.ProxyGraphics = Cache(cache); item.RemoveVertexAt(count); SameCache(null, item.ProxyGraphics);
            }
        }
        private static void ScalarBits(int cache)
        {
            foreach (bool retained in new[] { false, true }) foreach (double value in new[] {
                BitConverter.Int64BitsToDouble(long.MinValue), double.Epsilon, double.MaxValue,
                BitConverter.Int64BitsToDouble(Bits(1.0) + 1) }) for (int axis = 0; axis < 3; axis++)
            {
                var item = Subject(retained, out var document); var point = item.Vertexes[0];
                if (axis == 0) point.X = value; if (axis == 1) point.Y = value; if (axis == 2) point.Z = value;
                item.ProxyGraphics = Cache(cache); item.SetVertex(0, point); SameCache(null, item.ProxyGraphics); Check(Same(point, item.Vertexes[0]), "Accepted scalar bits changed");
                item.ProxyGraphics = Cache(cache); item.SetVertex(0, point); SameCache(Cache(cache), item.ProxyGraphics);
            }
        }
        private static void Clone(int cache)
        {
            foreach (bool retained in new[] { false, true })
            {
                var source = Subject(retained, out var original); source.ProxyGraphics = Cache(cache);
                var clone = (Polyline3D)source.Clone(); var target = new DxfDocument(DxfVersion.AutoCad2018); target.Entities.Add(clone);
                SameCache(Cache(cache), clone.ProxyGraphics); clone.SetVertex(1, new Vector3(20, 30, 40));
                SameCache(null, clone.ProxyGraphics); SameCache(Cache(cache), source.ProxyGraphics); SamePoints(Points(), source.Vertexes);
                Check(!ReferenceEquals(source.Vertexes, clone.Vertexes), "Clone aliases coordinate storage");
                if (retained) Check(!ReferenceEquals(source.EndSequenceRecord, clone.EndSequenceRecord)
                    && !source.VertexRecords.Intersect(clone.VertexRecords).Any(), "Clone aliases retained records");
            }
        }
        private static void ReferencedRemoval(int cache)
        {
            var item = Subject(true, out var document); var vertex = item.VertexRecords[1];
            var reference = new DxfXRecord(); reference.Data.Add(new DxfTag(330, vertex.Handle)); document.NamedObjects.Add("P3_REFERENCE", reference);
            item.ProxyGraphics = Cache(cache); string before = State(item); Refuses(() => item.RemoveVertexAt(1));
            Check(before == State(item) && ReferenceEquals(vertex.Owner, item) && ReferenceEquals(document.GetObjectByHandle(vertex.Handle), vertex), "Referenced-removal changed graph");
            SameCache(Cache(cache), item.ProxyGraphics);
            item.SetVertex(1, new Vector3(20, 30, 40)); SameCache(null, item.ProxyGraphics);
            Check(ReferenceEquals(document.GetObjectByHandle(vertex.Handle), vertex) && document.Objects.Validate().Count == 0, "Coordinate replacement disturbed incoming reference");
        }
        private static void RetainedState(int cache)
        {
            for (int fault = 0; fault < 4; fault++)
            {
                var item = Subject(true, out var document);
                if (fault == 0) item.Vertexes.Add(Vector3.Zero);
                if (fault == 1) item.SmoothType = PolylineSmoothType.Quadratic;
                if (fault == 2) item = (Polyline3D)item.Clone();
                if (fault == 3) document.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
                item.ProxyGraphics = Cache(cache); string before = State(item); Refuses(() => item.SetVertex(0, Vector3.Zero));
                Check(before == State(item), "Invalid retained-state edit changed state"); SameCache(Cache(cache), item.ProxyGraphics);
            }
        }

        private static void Apply(Polyline3D item, string operation)
        {
            switch (operation)
            {
                case "set": item.SetVertex(1, new Vector3(20, 30, 40)); break;
                case "zero": item.SetVertex(0, new Vector3(BitConverter.Int64BitsToDouble(long.MinValue), 2, 3)); break;
                case "insert": item.InsertVertex(1, new Vector3(20, 30, 40)); break;
                case "remove": item.RemoveVertexAt(1); break;
                case "move": item.MoveVertex(0, 2); break;
                case "reverse": item.Reverse(); break;
                case "closed": item.IsClosed = true; break;
                case "linetype": item.LinetypeGeneration = true; break;
                case "same-index": item.MoveVertex(1, 1); break;
                case "same-set": item.SetVertex(1, item.Vertexes[1]); break;
            }
        }
        private static Vector3[] Expected(string operation)
        {
            var points = Points().ToList();
            if (operation == "set") points[1] = new Vector3(20, 30, 40);
            if (operation == "zero") points[0] = new Vector3(BitConverter.Int64BitsToDouble(long.MinValue), 2, 3);
            if (operation == "insert") points.Insert(1, new Vector3(20, 30, 40));
            if (operation == "remove") points.RemoveAt(1);
            if (operation == "move") points = new[] { Points()[1], Points()[2], Points()[0], Points()[3] }.ToList();
            if (operation == "reverse") points.Reverse();
            return points.ToArray();
        }
        private static bool Changes(string operation) { return operation != "none" && operation != "same-index" && operation != "same-set"; }
        private static string Name(Polyline3D item) { return (string)item.XData["P3_EDITS"].XDataRecord.Single().Value; }
        private static void Wire(DxfVersion version, bool binary, string? directory)
        {
            var seed = new DxfDocument(version); var subjects = new List<Polyline3D>();
            for (int place = 0; place < 4; place++)
            {
                var items = new List<Polyline3D>();
                for (int cache = 0; cache < 3; cache++) foreach (string operation in Operations)
                {
                    var item = new Polyline3D(Points()); var data = new XData(new ApplicationRegistry("P3_EDITS"));
                    data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"P3_{place}_{cache}_{operation}")); item.XData.Add(data);
                    item.ProxyGraphics = Cache(cache); items.Add(item);
                }
                if (place == 0) foreach (var item in items) seed.Entities.Add(item);
                else if (place == 1) { var layout = new Layout("P3_PAPER"); seed.Layouts.Add(layout); foreach (var item in items) layout.AssociatedBlock.Entities.Add(item); }
                else { var block = new Block("P3_CONTAINER_" + place); foreach (var item in items) block.Entities.Add(item); if (place == 2) seed.Entities.Add(new Insert(block)); else seed.Blocks.Add(block); }
                subjects.AddRange(items);
            }
            var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(following);
            string prefix = "polyline3d-edits-" + version + "-" + (binary ? "binary" : "text");
            byte[] source = Save(seed, binary); if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source);
            var document = Load(source); var identities = new Dictionary<string, string[]>();
            foreach (var original in subjects)
            {
                var item = (Polyline3D)document.GetObjectByHandle(original.Handle); string[] parts = Name(item).Split('_'); string operation = parts[3]; int cache = int.Parse(parts[2], CultureInfo.InvariantCulture);
                SamePoints(Points(), item.Vertexes); SameCache(Cache(cache), item.ProxyGraphics);
                var end = item.EndSequenceRecord; var records = item.VertexRecords.ToList(); var list = item.Vertexes;
                Apply(item, operation); SamePoints(Expected(operation), item.Vertexes); SameCache(Changes(operation) ? null : Cache(cache), item.ProxyGraphics);
                Check(ReferenceEquals(end, item.EndSequenceRecord) && ReferenceEquals(list, item.Vertexes), "Wire edit replaced list/terminator");
                if (operation == "insert") records.Insert(1, item.VertexRecords[1]);
                if (operation == "remove") records.RemoveAt(1);
                if (operation == "move") records = new[] { records[1], records[2], records[0], records[3] }.ToList();
                if (operation == "reverse") records.Reverse();
                Check(records.SequenceEqual(item.VertexRecords), "Wire edit moved wrong record");
                identities.Add(item.Handle, item.VertexRecords.Select(record => record.Handle).Concat(new[] { item.EndSequenceRecord.Handle }).ToArray());
            }
            for (int stage = 0; stage < 2; stage++)
            {
                byte[] bytes = Save(document, stage == 0 ? binary : !binary);
                if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
                document = Load(bytes);
                foreach (var pair in identities)
                {
                    var item = (Polyline3D)document.GetObjectByHandle(pair.Key); string[] parts = Name(item).Split('_'); string operation = parts[3]; int cache = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    SamePoints(Expected(operation), item.Vertexes); SameCache(Changes(operation) ? null : Cache(cache), item.ProxyGraphics);
                    Check(item.IsClosed == (operation == "closed") && item.LinetypeGeneration == (operation == "linetype"), "Serialized flags");
                    Check(pair.Value.SequenceEqual(item.VertexRecords.Select(record => record.Handle).Concat(new[] { item.EndSequenceRecord.Handle })), "Serialized identities changed");
                    foreach (var record in item.VertexRecords.Concat(new[] { item.EndSequenceRecord }))
                        Check(ReferenceEquals(record.Owner, item) && ReferenceEquals(document.GetObjectByHandle(record.Handle), record), "Record registration/owner changed");
                }
                var line = (Line)document.GetObjectByHandle(following.Handle); Check(Same(line.StartPoint, following.StartPoint) && Same(line.EndPoint, following.EndPoint), "Following LINE changed");
                Check(document.Objects.Validate().Count == 0, "Edited wire graph validation");
            }
        }
    }
}
