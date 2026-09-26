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
    internal static class LegacyMeshEditCases
    {
        internal sealed class Case
        {
            internal readonly string Id;
            internal readonly Action Test;
            internal Case(string id, Action test) { Id = id; Test = test; }
        }
        private static readonly DxfVersion[] Versions = { DxfVersion.AutoCad2000, DxfVersion.AutoCad2004,
            DxfVersion.AutoCad2007, DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 };
        private static readonly string[] PolygonOps = { "none", "set", "zero", "closedU", "closedV", "densityU", "densityV", "same", "outside" };
        private static readonly string[] PolyfaceOps = { "none", "set", "zero", "index", "hidden", "visible", "terminate", "same", "sameEdge" };
        private static Vector3[] Points() { return Enumerable.Range(0, 9).Select(i => new Vector3(i % 3, i / 3, (i % 3) * (i / 3))).ToArray(); }
        private static byte[]? Cache(int kind) { return kind == 0 ? null : kind == 1 ? Array.Empty<byte>() : new byte[] { 77, 69, 83, 72, 0, 255 }; }
        private static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        private static bool Same(Vector3 a, Vector3 b) { return Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void SameCache(byte[]? expected, byte[]? actual)
        { Check(expected == null ? actual == null : actual != null && expected.SequenceEqual(actual), "Parent proxy cache differs"); }
        private static void SamePoints(Vector3[] expected, Vector3[] actual)
        { Check(expected.Length == actual.Length && expected.Zip(actual, Same).All(x => x), "Coordinate bits/order differ"); }
        private static void Refuses(Action action)
        { bool refused = false; try { action(); } catch (ArgumentException) { refused = true; } catch (InvalidOperationException) { refused = true; } catch (NotSupportedException) { refused = true; } Check(refused, "Invalid edit accepted"); }
        private static PolygonMesh Polygon() { return new PolygonMesh(3, 3, Points()) { DensityU = 4, DensityV = 5 }; }
        private static PolyfaceMesh Polyface()
        { return new PolyfaceMesh(Points(), new[] { new short[] { 1, 2, 5, 4 }, new short[] { -4, 5, 8, 7 }, new short[] { 2, 3, 6 } }); }
        private static byte[] Save(DxfDocument document, bool binary)
        { using (var stream = new MemoryStream()) { Check(document.Save(stream, binary) && stream.CanWrite, "Save/stream lifetime"); return stream.ToArray(); } }
        private static DxfDocument Load(byte[] source)
        {
            byte[] original = (byte[])source.Clone();
            using (var stream = new MemoryStream(source))
            {
                var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Typed load failed");
                Check(stream.CanRead && source.SequenceEqual(original), "Source bytes/stream lifetime"); return document;
            }
        }
        private static EntityObject Subject(bool polyface, bool retained, out DxfDocument document)
        {
            document = new DxfDocument(DxfVersion.AutoCad2018); EntityObject item = polyface ? (EntityObject)Polyface() : Polygon();
            document.Entities.Add(item);
            if (retained) { string handle = item.Handle; document = Load(Save(document, false)); item = (EntityObject)document.GetObjectByHandle(handle); }
            Check(Records(item).Any() == retained, "Invalid retained setup"); return item;
        }
        private static DxfObject[] Records(EntityObject item)
        {
            if (item is PolygonMesh grid) return grid.VertexRecords.Cast<DxfObject>().Concat(grid.EndSequenceRecord == null ? Array.Empty<DxfObject>() : new DxfObject[] { grid.EndSequenceRecord }).ToArray();
            return ((PolyfaceMesh)item).RecordSequence.Cast<DxfObject>().ToArray();
        }
        private static Vector3[] Coordinates(EntityObject item) { return item is PolygonMesh grid ? grid.Vertexes : ((PolyfaceMesh)item).Vertexes; }
        private static string State(EntityObject item)
        {
            string geometry = string.Join(";", Coordinates(item).Select(p => Bits(p.X) + "," + Bits(p.Y) + "," + Bits(p.Z)));
            if (item is PolygonMesh grid) geometry += "|" + grid.IsClosedInU + "," + grid.IsClosedInV + "," + grid.DensityU + "," + grid.DensityV + "," + grid.SmoothType;
            else geometry += "|" + string.Join(";", ((PolyfaceMesh)item).Faces.Select(face => string.Join(",", face.VertexIndexes)));
            return geometry + "|" + string.Join(",", Records(item).Select(r => r.Handle));
        }
        internal static IEnumerable<Case> All(string? directory)
        {
            foreach (bool retained in new[] { false, true }) for (int cache = 0; cache < 3; cache++)
            {
                bool stored = retained; int kind = cache;
                foreach (string operation in PolygonOps.Concat(new[] { "quadratic", "cubic", "bezier", "sameU", "sameV", "sameDensityU", "sameDensityV", "sameSmooth" }))
                { string op = operation; yield return new Case($"polygon/{retained}/{cache}/{op}", () => PolygonEdit(stored, kind, op)); }
                foreach (string operation in PolyfaceOps)
                { string op = operation; yield return new Case($"polyface/{retained}/{cache}/{op}", () => PolyfaceEdit(stored, kind, op)); }
                for (int fault = 0; fault < 18; fault++)
                { int f = fault; yield return new Case($"polygon-refused/{retained}/{cache}/{f}", () => PolygonRefused(stored, kind, f)); }
                for (int fault = 0; fault < 17; fault++)
                { int f = fault; yield return new Case($"polyface-refused/{retained}/{cache}/{f}", () => PolyfaceRefused(stored, kind, f)); }
                yield return new Case($"scalar-bits/{retained}/{cache}", () => ScalarBits(stored, kind));
                yield return new Case($"clone/{retained}/{cache}", () => Clone(stored, kind));
            }
            yield return new Case("polygon-nonsquare/authored", () => NonSquare(false));
            yield return new Case("polygon-nonsquare/retained", () => NonSquare(true));
            yield return new Case("edge-short-min", ShortMin);
            yield return new Case("face-slot-activation", Activation);
            yield return new Case("polygon-incremental-repair", Repair);
            yield return new Case("polyface-retained-state", RetainedState);
            yield return new Case("polyface-incoming-references", ReferencedEdit);
            foreach (var version in Versions) foreach (bool binary in new[] { false, true })
            {
                yield return new Case($"wire/{version}/{binary}", () => Wire(version, binary, directory));
                yield return new Case($"density/{version}/{binary}", () => DensityWire(version, binary, directory));
            }
        }
        internal static void VerifyInstalled()
        { foreach (var item in All(null)) try { item.Test(); } catch (Exception error) { throw new InvalidOperationException("Installed legacy mesh edit: " + item.Id, error); } }
        private static bool Changes(string operation)
        { return operation != "none" && operation != "outside" && !operation.StartsWith("same", StringComparison.Ordinal); }
        private static void ApplyPolygon(PolygonMesh grid, string operation)
        {
            switch (operation)
            {
                case "set": grid.SetVertex(1, 1, new Vector3(20, 30, 40)); break;
                case "zero": grid.SetVertex(0, 0, new Vector3(BitConverter.Int64BitsToDouble(long.MinValue), 0, 0)); break;
                case "closedU": grid.IsClosedInU = true; break;
                case "closedV": grid.IsClosedInV = true; break;
                case "densityU": grid.DensityU = 7; break;
                case "densityV": grid.DensityV = 8; break;
                case "quadratic": grid.SmoothType = PolylineSmoothType.Quadratic; break;
                case "cubic": grid.SmoothType = PolylineSmoothType.Cubic; break;
                case "bezier": grid.SmoothType = PolylineSmoothType.BezierSurface; break;
                case "same": grid.SetVertex(1, 1, grid.GetVertex(1, 1)); break;
                case "outside": grid.SetVertex(int.MaxValue, int.MinValue, new Vector3(double.NaN, double.PositiveInfinity, 0)); break;
                case "sameU": grid.IsClosedInU = grid.IsClosedInU; break;
                case "sameV": grid.IsClosedInV = grid.IsClosedInV; break;
                case "sameDensityU": grid.DensityU = grid.DensityU; break;
                case "sameDensityV": grid.DensityV = grid.DensityV; break;
                case "sameSmooth": grid.SmoothType = grid.SmoothType; break;
            }
        }
        private static void ApplyPolyface(PolyfaceMesh mesh, string operation)
        {
            switch (operation)
            {
                case "set": mesh.SetVertex(4, new Vector3(20, 30, 40)); break;
                case "zero": mesh.SetVertex(0, new Vector3(BitConverter.Int64BitsToDouble(long.MinValue), 0, 0)); break;
                case "index": mesh.SetFaceVertexIndex(0, 2, 8); break;
                case "hidden": mesh.SetFaceEdgeVisibility(0, 0, false); break;
                case "visible": mesh.SetFaceEdgeVisibility(1, 0, true); break;
                case "terminate": mesh.SetFaceVertexIndex(0, 3, 0); break;
                case "same": mesh.SetVertex(4, mesh.Vertexes[4]); mesh.SetFaceVertexIndex(0, 0, 1); break;
                case "sameEdge": mesh.SetFaceEdgeVisibility(1, 0, false); break;
            }
        }
        private static void VerifyGeometry(EntityObject item, string operation)
        {
            Vector3[] points = Points(); if (operation == "set") points[4] = new Vector3(20, 30, 40);
            if (operation == "zero") points[0] = new Vector3(BitConverter.Int64BitsToDouble(long.MinValue), 0, 0);
            SamePoints(points, Coordinates(item));
            if (item is PolygonMesh grid)
            {
                Check(grid.IsClosedInU == (operation == "closedU") && grid.IsClosedInV == (operation == "closedV"), "Closure changed");
                Check(grid.DensityU == (operation == "densityU" ? 7 : 4) && grid.DensityV == (operation == "densityV" ? 8 : 5), "Density changed");
                var smooth = operation == "quadratic" ? PolylineSmoothType.Quadratic : operation == "cubic" ? PolylineSmoothType.Cubic : operation == "bezier" ? PolylineSmoothType.BezierSurface : PolylineSmoothType.NoSmooth;
                Check(grid.SmoothType == smooth && grid.U == 3 && grid.V == 3, "Smoothing/dimensions changed");
            }
            else
            {
                var faces = Polyface().Faces.Select(f => (short[])f.VertexIndexes.Clone()).ToArray();
                if (operation == "index") faces[0][2] = 8; if (operation == "hidden") faces[0][0] = -1;
                if (operation == "visible") faces[1][0] = 4; if (operation == "terminate") faces[0][3] = 0;
                var actual = ((PolyfaceMesh)item).Faces;
                for (int i = 0; i < faces.Length; i++) Check(faces[i].TakeWhile(index => index != 0).SequenceEqual(actual[i].VertexIndexes.TakeWhile(index => index != 0)), "Signed face indices changed");
            }
        }
        private static void PolygonEdit(bool retained, int cache, string operation)
        {
            var grid = (PolygonMesh)Subject(false, retained, out var document); var array = grid.Vertexes; var records = Records(grid); var owner = grid.Owner;
            grid.ProxyGraphics = Cache(cache); ApplyPolygon(grid, operation); VerifyGeometry(grid, operation);
            SameCache(Changes(operation) ? null : Cache(cache), grid.ProxyGraphics);
            Check(ReferenceEquals(array, grid.Vertexes) && ReferenceEquals(owner, grid.Owner) && records.SequenceEqual(Records(grid)), "Polygon identity changed");
            foreach (var record in records) Check(ReferenceEquals(document.GetObjectByHandle(record.Handle), record) && ReferenceEquals(record.Owner, grid), "Polygon registration changed");
        }
        private static void PolyfaceEdit(bool retained, int cache, string operation)
        {
            var mesh = (PolyfaceMesh)Subject(true, retained, out var document); var array = mesh.Vertexes; var records = Records(mesh);
            var faces = mesh.Faces.ToArray(); var slots = faces.Select(f => f.VertexIndexes).ToArray(); var owner = mesh.Owner;
            mesh.ProxyGraphics = Cache(cache); ApplyPolyface(mesh, operation); VerifyGeometry(mesh, operation);
            SameCache(Changes(operation) ? null : Cache(cache), mesh.ProxyGraphics);
            Check(ReferenceEquals(array, mesh.Vertexes) && ReferenceEquals(owner, mesh.Owner) && records.SequenceEqual(Records(mesh)) && faces.SequenceEqual(mesh.Faces), "Polyface identity changed");
            for (int i = 0; i < faces.Length; i++) Check(ReferenceEquals(slots[i], faces[i].VertexIndexes), "Face index array replaced");
            foreach (var record in records) Check(ReferenceEquals(document.GetObjectByHandle(record.Handle), record) && ReferenceEquals(record.Owner, mesh), "Polyface registration changed");
            Check(document.Objects.Validate().Count == 0, "Polyface graph invalid");
        }
        private static Vector3 Nonfinite(int fault)
        { var p = new Vector3(20, 30, 40); double x = fault % 3 == 0 ? double.NaN : fault % 3 == 1 ? double.PositiveInfinity : double.NegativeInfinity; if (fault / 3 == 0) p.X = x; else if (fault / 3 == 1) p.Y = x; else p.Z = x; return p; }
        private static void PolygonRefused(bool retained, int cache, int fault)
        {
            var grid = (PolygonMesh)Subject(false, retained, out var document); grid.ProxyGraphics = Cache(cache); string before = State(grid);
            Refuses(() =>
            {
                if (fault < 9) grid.SetVertex(1, 1, Nonfinite(fault));
                else if (fault < 13) grid.DensityU = new short[] { -1, 0, 2, 202 }[fault - 9];
                else if (fault < 17) grid.DensityV = new short[] { -1, 0, 2, 202 }[fault - 13];
                else grid.SmoothType = (PolylineSmoothType)7;
            });
            Check(before == State(grid), "Refused polygon edit changed state"); SameCache(Cache(cache), grid.ProxyGraphics);
        }
        private static void PolyfaceRefused(bool retained, int cache, int fault)
        {
            var mesh = (PolyfaceMesh)Subject(true, retained, out var document); mesh.ProxyGraphics = Cache(cache); string before = State(mesh);
            Refuses(() =>
            {
                if (fault < 9) mesh.SetVertex(1, Nonfinite(fault));
                else switch (fault)
                {
                    case 9: mesh.SetVertex(-1, Vector3.Zero); break;
                    case 10: mesh.SetVertex(9, Vector3.Zero); break;
                    case 11: mesh.SetFaceVertexIndex(-1, 0, 1); break;
                    case 12: mesh.SetFaceVertexIndex(0, 4, 1); break;
                    case 13: mesh.SetFaceVertexIndex(0, 0, 0); break;
                    case 14: mesh.SetFaceVertexIndex(0, 1, 10); break;
                    case 15: mesh.SetFaceVertexIndex(0, 1, short.MinValue); break;
                    case 16: mesh.SetFaceEdgeVisibility(2, 3, false); break;
                }
            });
            Check(before == State(mesh), "Refused polyface edit changed state"); SameCache(Cache(cache), mesh.ProxyGraphics);
        }
        private static void ScalarBits(bool retained, int cache)
        {
            foreach (bool polyface in new[] { false, true }) foreach (double value in new[] { BitConverter.Int64BitsToDouble(long.MinValue), double.Epsilon, double.MaxValue, BitConverter.Int64BitsToDouble(Bits(1.0) + 1) })
            {
                var item = Subject(polyface, retained, out var document); var p = new Vector3(value, value, value); item.ProxyGraphics = Cache(cache);
                if (item is PolygonMesh grid) grid.SetVertex(0, 0, p); else ((PolyfaceMesh)item).SetVertex(0, p);
                Check(Same(Coordinates(item)[0], p), "Scalar bits changed"); SameCache(null, item.ProxyGraphics); item.ProxyGraphics = Cache(cache);
                if (item is PolygonMesh same) same.SetVertex(0, 0, p); else ((PolyfaceMesh)item).SetVertex(0, p);
                SameCache(Cache(cache), item.ProxyGraphics);
            }
        }
        private static void Clone(bool retained, int cache)
        {
            foreach (bool polyface in new[] { false, true })
            {
                var source = Subject(polyface, retained, out var original); source.ProxyGraphics = Cache(cache); string before = State(source);
                var clone = (EntityObject)source.Clone(); var target = new DxfDocument(DxfVersion.AutoCad2018); target.Entities.Add(clone); SameCache(Cache(cache), clone.ProxyGraphics);
                if (clone is PolygonMesh grid) grid.SetVertex(1, 1, new Vector3(20, 30, 40)); else ((PolyfaceMesh)clone).SetVertex(4, new Vector3(20, 30, 40));
                VerifyGeometry(clone, "set"); SameCache(null, clone.ProxyGraphics); Check(before == State(source), "Clone edited source"); SameCache(Cache(cache), source.ProxyGraphics);
                Check(!ReferenceEquals(Coordinates(source), Coordinates(clone)) && !Records(source).Intersect(Records(clone)).Any(), "Clone aliases records/storage");
            }
        }
        private static void ShortMin()
        {
            var mesh = new PolyfaceMesh(Enumerable.Repeat(Vector3.Zero, 32768), new[] { new short[] { short.MinValue, 1, 2 } });
            mesh.ProxyGraphics = Cache(2); string before = State(mesh); mesh.SetFaceEdgeVisibility(0, 0, false); SameCache(Cache(2), mesh.ProxyGraphics);
            Refuses(() => mesh.SetFaceEdgeVisibility(0, 0, true)); Check(before == State(mesh), "Unrepresentable positive index mutated state"); SameCache(Cache(2), mesh.ProxyGraphics);
        }
        private static void Activation()
        {
            var mesh = new PolyfaceMesh(Points(), new[] { new short[] { 1, 2, 0, 100 } });
            mesh.ProxyGraphics = Cache(2); string before = State(mesh); Refuses(() => mesh.SetFaceVertexIndex(0, 2, 3)); Check(before == State(mesh), "Activated invalid tail"); SameCache(Cache(2), mesh.ProxyGraphics);
            mesh.SetFaceVertexIndex(0, 3, 4); SameCache(null, mesh.ProxyGraphics); mesh.SetFaceVertexIndex(0, 2, 3);
            Check(mesh.Faces[0].VertexIndexes.SequenceEqual(new short[] { 1, 2, 3, 4 }), "Valid tail activation failed");
        }
        private static void Repair()
        {
            var grid = Polygon(); grid.Vertexes[0] = Nonfinite(0); grid.Vertexes[1] = Nonfinite(1); grid.ProxyGraphics = Cache(2);
            grid.SetVertex(0, 0, Points()[0]); SameCache(null, grid.ProxyGraphics); grid.SetVertex(1, 0, Points()[1]); SamePoints(Points(), grid.Vertexes);
            grid.ProxyGraphics = Cache(2); grid.SetVertex(-1, 0, Nonfinite(0)); grid.SetVertex(0, 3, Nonfinite(0)); SameCache(Cache(2), grid.ProxyGraphics);
            Check(Same(grid.GetVertex(-1, 0), grid.Vertexes[0]), "Historical invalid getter fallback changed");
        }
        private static void RetainedState()
        {
            for (int fault = 0; fault < 4; fault++)
            {
                var mesh = (PolyfaceMesh)Subject(true, true, out var document);
                if (fault == 0) mesh = (PolyfaceMesh)mesh.Clone();
                if (fault == 1) document.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
                if (fault == 2) mesh.Vertexes[8] = Nonfinite(0);
                if (fault == 3) mesh.Faces[2].VertexIndexes[0] = 100;
                mesh.ProxyGraphics = Cache(2); string before = State(mesh);
                Refuses(() => mesh.SetVertex(0, Vector3.Zero)); Refuses(() => mesh.SetFaceVertexIndex(0, 0, 1));
                Check(before == State(mesh), "Invalid retained-state edit changed mesh"); SameCache(Cache(2), mesh.ProxyGraphics);
            }
        }
        private static void ReferencedEdit()
        {
            var mesh = (PolyfaceMesh)Subject(true, true, out var document); var vertices = mesh.VertexRecords.ToArray(); var faces = mesh.FaceRecords.ToArray();
            var reference = new DxfXRecord(); reference.Data.Add(new DxfTag(330, vertices[4].Handle)); reference.Data.Add(new DxfTag(330, faces[0].Handle)); document.NamedObjects.Add("MESH_REFERENCE", reference);
            mesh.SetVertex(4, new Vector3(20, 30, 40)); mesh.SetFaceEdgeVisibility(0, 0, false);
            Check(ReferenceEquals(document.GetObjectByHandle(vertices[4].Handle), vertices[4]) && ReferenceEquals(document.GetObjectByHandle(faces[0].Handle), faces[0]), "Incoming reference target replaced");
            Check(document.Objects.Validate().Count == 0, "Incoming reference graph invalid");
        }

        private static string Name(EntityObject item) { return (string)item.XData["MESH_EDITS"].XDataRecord.Single().Value; }
        private static void Wire(DxfVersion version, bool binary, string? directory)
        {
            var seed = new DxfDocument(version); var subjects = new List<EntityObject>();
            for (int place = 0; place < 4; place++)
            {
                var items = new List<EntityObject>();
                foreach (bool polyface in new[] { false, true }) for (int cache = 0; cache < 3; cache++) foreach (string operation in polyface ? PolyfaceOps : PolygonOps)
                {
                    EntityObject item = polyface ? (EntityObject)Polyface() : Polygon(); string name = $"ME_{(polyface ? "face" : "grid")}_{place}_{cache}_{operation}";
                    var data = new XData(new ApplicationRegistry("MESH_EDITS")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, name)); item.XData.Add(data); item.ProxyGraphics = Cache(cache); items.Add(item);
                }
                if (place == 0) foreach (var item in items) seed.Entities.Add(item);
                else if (place == 1) { var layout = new Layout("ME_PAPER"); seed.Layouts.Add(layout); foreach (var item in items) layout.AssociatedBlock.Entities.Add(item); }
                else { var block = new Block("ME_CONTAINER_" + place); foreach (var item in items) block.Entities.Add(item); if (place == 2) seed.Entities.Add(new Insert(block)); else seed.Blocks.Add(block); }
                subjects.AddRange(items);
            }
            var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(following);
            // Load once to obtain real registered child identities, then attach metadata before the recorded source.
            var document = Load(Save(seed, binary));
            foreach (var original in subjects)
            {
                var item = (EntityObject)document.GetObjectByHandle(original.Handle); int index = 0;
                foreach (var record in Records(item))
                {
                    var data = new XData(new ApplicationRegistry("ME_CHILD")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, Name(item) + "/" + index++)); record.XData.Add(data);
                }
            }
            string prefix = "legacy-mesh-edits-" + version + "-" + (binary ? "binary" : "text"); byte[] source = Save(document, binary);
            if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source); document = Load(source);
            var identities = new Dictionary<string, string[]>();
            foreach (var original in subjects)
            {
                var item = (EntityObject)document.GetObjectByHandle(original.Handle); string[] parts = Name(item).Split('_'); string operation = parts[4]; int cache = int.Parse(parts[3], CultureInfo.InvariantCulture);
                var records = Records(item); SameCache(Cache(cache), item.ProxyGraphics);
                if (item is PolygonMesh grid) ApplyPolygon(grid, operation); else ApplyPolyface((PolyfaceMesh)item, operation);
                VerifyGeometry(item, operation); SameCache(Changes(operation) ? null : Cache(cache), item.ProxyGraphics); Check(records.SequenceEqual(Records(item)), "Wire edit replaced children");
                identities.Add(item.Handle, records.Select(r => r.Handle).ToArray());
            }
            for (int stage = 0; stage < 2; stage++)
            {
                byte[] bytes = Save(document, stage == 0 ? binary : !binary);
                if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes); document = Load(bytes);
                foreach (var pair in identities)
                {
                    var item = (EntityObject)document.GetObjectByHandle(pair.Key); string[] parts = Name(item).Split('_'); string operation = parts[4]; int cache = int.Parse(parts[3], CultureInfo.InvariantCulture);
                    VerifyGeometry(item, operation); SameCache(Changes(operation) ? null : Cache(cache), item.ProxyGraphics); var records = Records(item);
                    Check(pair.Value.SequenceEqual(records.Select(r => r.Handle)), "Saved child identity changed");
                    for (int i = 0; i < records.Length; i++) Check(ReferenceEquals(records[i].Owner, item) && ReferenceEquals(document.GetObjectByHandle(records[i].Handle), records[i])
                        && (string)records[i].XData["ME_CHILD"].XDataRecord.Single().Value == Name(item) + "/" + i, "Child owner/metadata changed");
                }
                var line = (Line)document.GetObjectByHandle(following.Handle); Check(Same(line.StartPoint, following.StartPoint) && Same(line.EndPoint, following.EndPoint), "Following LINE changed");
                Check(document.Objects.Validate().Count == 0, "Wire graph invalid");
            }
        }
        private static readonly short?[][] DensityProfiles = {
            new short?[] { null, null }, new short?[] { null, 5 }, new short?[] { 4, null },
            new short?[] { 0, 0 }, new short?[] { 1, 1 }, new short?[] { 2, 2 },
            new short?[] { -1, -2 }, new short?[] { short.MinValue, short.MaxValue },
            new short?[] { short.MaxValue, short.MinValue }, new short?[] { 201, 201 },
            new short?[] { 202, 203 }, new short?[] { 4, 5 } };

        private static void NonSquare(bool retained)
        {
            var points = Enumerable.Range(0, 12).Select(i => new Vector3(i, i * 2, -i)).ToArray();
            var item = new PolygonMesh(3, 4, points); var document = new DxfDocument(DxfVersion.AutoCad2018);
            document.Entities.Add(item);
            if (retained) { string handle = item.Handle; document = Load(Save(document, true)); item = (PolygonMesh)document.GetObjectByHandle(handle); }
            var records = Records(item); var array = item.Vertexes;
            for (int v = 0; v < 4; v++) for (int u = 0; u < 3; u++)
            {
                int slot = u + 3 * v; Check(Same(item.GetVertex(u, v), points[slot]), "Non-square source slot");
                var value = new Vector3(100 + slot, 200 + slot, 300 + slot); item.ProxyGraphics = Cache(2);
                item.SetVertex(u, v, value); points[slot] = value; SamePoints(points, item.Vertexes); SameCache(null, item.ProxyGraphics);
                Check(ReferenceEquals(array, item.Vertexes) && records.SequenceEqual(Records(item)), "Non-square identity/order");
                item.ProxyGraphics = Cache(1); item.SetVertex(u, v, value); SameCache(Cache(1), item.ProxyGraphics);
            }
            string identity = item.Handle; document = Load(Save(document, false)); item = (PolygonMesh)document.GetObjectByHandle(identity);
            SamePoints(points, item.Vertexes); Check(item.U == 3 && item.V == 4 && document.Objects.Validate().Count == 0, "Non-square round trip");
        }

        private static void DensityWire(DxfVersion version, bool binary, string? directory)
        {
            var seed = new DxfDocument(version); var handles = new List<string>();
            for (int i = 0; i < DensityProfiles.Length; i++)
            {
                var item = Polygon(); var data = new XData(new ApplicationRegistry("ME_DENSITY"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, "MD_" + i)); item.XData.Add(data);
                item.ProxyGraphics = Cache(i % 3); seed.Entities.Add(item); handles.Add(item.Handle);
            }
            var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(following);
            string[] lines = System.Text.Encoding.UTF8.GetString(Save(seed, false)).Replace("\r\n", "\n").Split('\n');
            var output = new List<string>(); int subject = -1, edited = 0; bool parent = false;
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                int code = int.Parse(lines[i], CultureInfo.InvariantCulture);
                if (code == 0) { parent = lines[i + 1] == "POLYLINE"; if (parent) subject++; }
                if (parent && (code == 73 || code == 74))
                {
                    edited++; short? value = DensityProfiles[subject][code - 73];
                    if (!value.HasValue) continue;
                    output.Add(lines[i]); output.Add(value.Value.ToString(CultureInfo.InvariantCulture)); continue;
                }
                output.Add(lines[i]); output.Add(lines[i + 1]);
            }
            Check(subject + 1 == DensityProfiles.Length && edited == DensityProfiles.Length * 2, "Density injection inventory");
            byte[] source = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", output) + "\n");
            string prefix = "legacy-mesh-density-" + version + "-" + (binary ? "binary" : "text");
            if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + "-source.dxf"), source);
            var document = Load(source); var identities = new Dictionary<string, string[]>();
            foreach (string handle in handles) identities.Add(handle, Records((EntityObject)document.GetObjectByHandle(handle)).Select(r => r.Handle).ToArray());
            for (int stage = 0; stage < 3; stage++)
            {
                for (int i = 0; i < handles.Count; i++)
                {
                    var item = (PolygonMesh)document.GetObjectByHandle(handles[i]);
                    short u = DensityProfiles[i][0] ?? 0, v = DensityProfiles[i][1] ?? 0;
                    Check(item.DensityU == u && item.DensityV == v, "Stored density short values changed: " + i);
                    SamePoints(Points(), item.Vertexes); SameCache(Cache(i % 3), item.ProxyGraphics);
                    Check(item.U == 3 && item.V == 3 && item.SmoothType == PolylineSmoothType.NoSmooth, "Density changed active geometry");
                    Check(identities[item.Handle].SequenceEqual(Records(item).Select(r => r.Handle)), "Density round trip changed child identities");
                    var clone = (PolygonMesh)item.Clone(); Check(clone.DensityU == u && clone.DensityV == v, "Clone lost stored density"); SameCache(Cache(i % 3), clone.ProxyGraphics);
                    if (u >= 3 && u <= 201) item.DensityU = u; else Refuses(() => item.DensityU = u);
                    if (v >= 3 && v <= 201) item.DensityV = v; else Refuses(() => item.DensityV = v);
                    SameCache(Cache(i % 3), item.ProxyGraphics);
                }
                var line = (Line)document.GetObjectByHandle(following.Handle);
                Check(Same(line.StartPoint, following.StartPoint) && Same(line.EndPoint, following.EndPoint), "Density following entity changed");
                Check(document.Objects.Validate().Count == 0, "Density graph invalid");
                if (stage == 2) break;
                byte[] bytes = Save(document, stage == 0 ? binary : !binary);
                if (directory != null) File.WriteAllBytes(Path.Combine(directory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
                document = Load(bytes);
            }
        }
    }
}
