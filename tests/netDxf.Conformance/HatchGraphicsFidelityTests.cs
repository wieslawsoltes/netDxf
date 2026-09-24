// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly byte[] HatchGraphicsBytes = Enumerable.Range(0, 300).Select(i => (byte)(i * 37)).ToArray();
    private static readonly string[] HatchGraphicsEdits = {
        "unchanged", "same-elevation", "same-pattern", "identity3", "identity4", "elevation",
        "translate3", "translate4", "scale", "pattern", "add", "remove", "replace", "clear-add"
    };

    private static HatchBoundaryPath HatchGraphicsPath(bool polyline, double offset = 0)
    {
        Vector2[] points = { new(offset, 0), new(offset + 4, 0), new(offset + 4, 3), new(offset, 3) };
        if (polyline)
            return new HatchBoundaryPath(new HatchBoundaryPath.Edge[] {
                new HatchBoundaryPath.Polyline { IsClosed = true, Vertexes = points.Select(p => new Vector3(p.X, p.Y, 0)).ToArray() }
            });
        return new HatchBoundaryPath(Enumerable.Range(0, 4).Select(i => (HatchBoundaryPath.Edge)
            new HatchBoundaryPath.Line { Start = points[i], End = points[(i + 1) % 4] }));
    }

    private static Hatch HatchGraphicsSubject(bool polyline, bool associative = false)
    {
        var hatch = new Hatch(HatchPattern.Solid, new[] { HatchGraphicsPath(polyline) }, associative) {
            Elevation = 2.5, PixelSize = .0625, Color = new AciColor(4), Layer = new Layer("HATCH_GRAPHICS")
        };
        hatch.SeedPoints.Clear(); hatch.SeedPoints.Add(new Vector2(2, 1));
        var data = new XData(new ApplicationRegistry("HATCH_GRAPHICS_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); hatch.XData.Add(data);
        return hatch;
    }

    private static void HatchGraphicsEdit(Hatch hatch, int edit, bool polyline)
    {
        switch (edit)
        {
            case 0: break;
            case 1: hatch.Elevation = hatch.Elevation; break;
            case 2: hatch.Pattern = hatch.Pattern; break;
            case 3: hatch.TransformBy(Matrix3.Identity, Vector3.Zero); break;
            case 4: hatch.TransformBy(Matrix4.Identity); break;
            case 5: hatch.Elevation = 5; break;
            case 6: hatch.TransformBy(Matrix3.Identity, new Vector3(7, -11, 13)); break;
            case 7: hatch.TransformBy(LineReviewMatrix4(Matrix3.Identity, new Vector3(7, -11, 13))); break;
            case 8: hatch.TransformBy(Matrix3.Scale(2), Vector3.Zero); break;
            case 9: hatch.Pattern = HatchPattern.Line; break;
            case 10: hatch.BoundaryPaths.Add(HatchGraphicsPath(polyline, 10)); break;
            case 11: hatch.BoundaryPaths.RemoveAt(1); break;
            case 12: hatch.BoundaryPaths[0] = HatchGraphicsPath(polyline, 3); break;
            case 13: hatch.BoundaryPaths.Clear(); hatch.BoundaryPaths.Add(HatchGraphicsPath(polyline, 3)); break;
            default: throw new InvalidOperationException("Unknown hatch edit.");
        }
    }

    private static void RegisterHatchGraphicsFidelityTests()
    {
        foreach (bool polyline in new[] { false, true }) foreach (bool associative in new[] { false, true })
        for (int payload = 0; payload < 3; payload++) for (int edit = 0; edit < HatchGraphicsEdits.Length; edit++)
        {
            int e = edit, p = payload;
            Run($"hatch-graphics/edit/{polyline}/{associative}/{p}/{HatchGraphicsEdits[e]}", () => {
                var hatch = HatchGraphicsSubject(polyline, associative); var doc = new DxfDocument();
                if (e == 11) hatch.BoundaryPaths.Add(HatchGraphicsPath(polyline, 10));
                doc.Entities.Add(hatch);
                hatch.ProxyGraphics = p == 0 ? null : p == 1 ? Array.Empty<byte>() : HatchGraphicsBytes;
                string handle = hatch.Handle; var owner = hatch.Owner;
                var original = (Hatch)hatch.Clone(); var unrelated = hatch.XData["HATCH_GRAPHICS_KEEP"];
                var oldPath = hatch.BoundaryPaths[0]; var pattern = hatch.Pattern;
                var sources = hatch.BoundaryPaths.SelectMany(path => path.Entities).ToArray();
                var sourceGeometry = sources.Select(source => (EntityObject)source.Clone()).ToArray();
                HatchGraphicsEdit(hatch, e, polyline);
                if (e < 5) Check(HatchGraphicsSameProxy(original.ProxyGraphics, hatch.ProxyGraphics), "No-op cache state changed");
                else Check(hatch.ProxyGraphics == null, "Edited hatch retained stale proxy graphics");
                Equal(handle, hatch.Handle, "Hatch handle changed"); Check(ReferenceEquals(owner, hatch.Owner), "Owner changed");
                Check(ReferenceEquals(unrelated, hatch.XData["HATCH_GRAPHICS_KEEP"]), "Unrelated XData identity changed");
                Equal("unchanged", (string)unrelated.XDataRecord.Single().Value, "Unrelated XData changed");
                SameDoubleBits(.0625, hatch.PixelSize!.Value, "Transform changed sampling hint");
                if (e < 5) {
                    SameDoubleBits(original.Elevation, hatch.Elevation, "No-op elevation changed");
                    Check(ReferenceEquals(pattern, hatch.Pattern) && ReferenceEquals(oldPath, hatch.BoundaryPaths[0]), "No-op replaced geometry objects");
                }
                // Existing successful TransformBy, including identity, explicitly detaches associations.
                Equal(associative && e is not (3 or 4 or 6 or 7 or 8), hatch.Associative, "Associativity contract changed");
                for (int i = 0; i < sources.Length; i++) {
                    if (sources[i] is Line line) {
                        RawLinePointBits(((Line)sourceGeometry[i]).StartPoint, line.StartPoint);
                        RawLinePointBits(((Line)sourceGeometry[i]).EndPoint, line.EndPoint);
                    }
                    Check(ReferenceEquals(e is 12 or 13 ? null : owner, sources[i].Owner), "Boundary source ownership policy changed");
                }
                Check(HatchGraphicsSameProxy(p == 0 ? null : p == 1 ? Array.Empty<byte>() : HatchGraphicsBytes, original.ProxyGraphics), "Clone source cache changed");
                Equal(0, doc.Objects.Validate().Count, "Hatch object graph invalid");
            });
        }

        foreach (bool associative in new[] { false, true }) foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        for (int component = 0; component < 16; component++) {
            int c = component; double bad = invalid;
            Run($"hatch-graphics/reject/nonfinite/{associative}/{c}/{ParameterBits(bad)}", () => {
                var hatch = HatchGraphicsSubject(true, associative); var matrix = Matrix4.Identity; matrix[c / 4, c % 4] = bad;
                HatchGraphicsReject(hatch, () => hatch.TransformBy(matrix));
            });
        }
        foreach (bool associative in new[] { false, true }) for (int component = 0; component < 4; component++)
        foreach (double value in new[] { double.Epsilon, 1.0, -1.0 }) {
            int c = component; double v = value;
            Run($"hatch-graphics/reject/projective/{associative}/{c}/{ParameterBits(v)}", () => {
                var hatch = HatchGraphicsSubject(true, associative); var matrix = LineReviewMatrix4(Matrix3.Scale(2), new Vector3(7, 11, 13));
                matrix[3, c] = c == 3 ? v == 1 ? 2 : v : v;
                HatchGraphicsReject(hatch, () => hatch.TransformBy(matrix));
            });
        }
        foreach (bool polyline in new[] { false, true }) {
            Run($"hatch-graphics/reject/null-pattern/{polyline}", () => {
                var hatch = HatchGraphicsSubject(polyline); HatchGraphicsReject(hatch, () => hatch.Pattern = null!);
            });
            Run($"hatch-graphics/reject/duplicate-path/{polyline}", () => {
                var hatch = HatchGraphicsSubject(polyline); HatchGraphicsReject(hatch, () => hatch.BoundaryPaths.Add(hatch.BoundaryPaths[0]));
            });
            Run($"hatch-graphics/cancelled-topology/{polyline}", () => {
                var hatch = HatchGraphicsSubject(polyline); hatch.ProxyGraphics = HatchGraphicsBytes;
                hatch.BoundaryPaths.BeforeAddItem += (_, args) => args.Cancel = true;
                Throws<ArgumentException>(() => hatch.BoundaryPaths.Add(HatchGraphicsPath(polyline, 10)));
                Equal(1, hatch.BoundaryPaths.Count, "Cancelled add published topology");
                Check(HatchGraphicsSameProxy(HatchGraphicsBytes, hatch.ProxyGraphics), "Cancelled add invalidated cache");
                hatch.BoundaryPaths.BeforeRemoveItem += (_, args) => args.Cancel = true;
                hatch.BoundaryPaths.RemoveAt(0);
                Equal(1, hatch.BoundaryPaths.Count, "Cancelled remove published topology");
                Check(HatchGraphicsSameProxy(HatchGraphicsBytes, hatch.ProxyGraphics), "Cancelled remove invalidated cache");
            });
            foreach (bool remove in new[] { false, true }) {
                bool r = remove;
                Run($"hatch-graphics/callback/{polyline}/{r}", () => {
                    var hatch = HatchGraphicsSubject(polyline); hatch.ProxyGraphics = HatchGraphicsBytes; int calls = 0;
                    void Changed(Hatch sender, ObservableCollectionEventArgs<HatchBoundaryPath> args) {
                        calls++; Check(sender.ProxyGraphics == null, "Observer saw stale graphics after committed topology change");
                        throw new InvalidOperationException("injected caller failure");
                    }
                    if (r) hatch.HatchBoundaryPathRemoved += Changed; else hatch.HatchBoundaryPathAdded += Changed;
                    Throws<InvalidOperationException>(() => { if (r) hatch.BoundaryPaths.RemoveAt(0); else hatch.BoundaryPaths.Add(HatchGraphicsPath(polyline, 10)); });
                    Equal(1, calls, "Boundary event count"); Equal(r ? 0 : 2, hatch.BoundaryPaths.Count, "Committed topology missing after caller error");
                    Check(hatch.ProxyGraphics == null, "Caller error restored stale cache");
                });
            }
        }
        foreach (double value in new[] { -0.0, double.Epsilon, -double.Epsilon, 2.5, double.NaN, double.PositiveInfinity }) {
            double v = value;
            Run($"hatch-graphics/elevation-bits/{ParameterBits(v)}", () => {
                var hatch = HatchGraphicsSubject(true); hatch.Elevation = v; hatch.ProxyGraphics = HatchGraphicsBytes;
                hatch.Elevation = v; SameDoubleBits(v, hatch.Elevation, "Same elevation changed bits");
                Check(HatchGraphicsSameProxy(HatchGraphicsBytes, hatch.ProxyGraphics), "Bit-identical assignment cleared cache");
                hatch.Elevation = 0.0; SameDoubleBits(0.0, hatch.Elevation, "Elevation setter admission changed");
                Check(hatch.ProxyGraphics == null, "Different elevation bits retained cache");
            });
        }
        Run("hatch-graphics/matrix4-dispatch", () => {
            var hatch = new HatchGraphicsDispatchProbe();
            var bad = Matrix4.Identity; bad.M41 = double.Epsilon;
            Throws<NotSupportedException>(() => hatch.TransformBy(bad)); Equal(0, hatch.Calls, "Projective input reached virtual affine method");
            hatch.TransformBy(Matrix4.Identity); Equal(1, hatch.Calls, "Valid matrix no longer uses virtual affine method");
        });
        foreach (int shape in Enumerable.Range(0, 3)) {
            int s = shape;
            Run($"hatch-graphics/conic-spline-transform/{s}", () => {
                HatchBoundaryPath.Edge edge = s == 0
                    ? new HatchBoundaryPath.Arc { Center = new Vector2(2, 1), Radius = 2, StartAngle = 0, EndAngle = 360, IsCounterclockwise = true }
                    : s == 1 ? new HatchBoundaryPath.Ellipse { Center = new Vector2(2, 1), EndMajorAxis = new Vector2(3, 0), MinorRatio = .5, StartAngle = 0, EndAngle = 360, IsCounterclockwise = true }
                    : new HatchBoundaryPath.Spline { Degree = 2, IsRational = false, IsPeriodic = false,
                        Knots = new[] { 0.0, 0, 0, 1, 1, 1 }, ControlPoints = new[] { new Vector3(0, 0, 1), new Vector3(2, 3, 1), new Vector3(4, 0, 1) } };
                var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false);
                hatch.ProxyGraphics = HatchGraphicsBytes; hatch.TransformBy(Matrix3.Scale(2), new Vector3(5, 7, 0));
                Check(hatch.ProxyGraphics == null, "Conic/spline transform retained graphics"); Equal(edge.Type, hatch.BoundaryPaths[0].Edges[0].Type, "Similarity changed boundary family");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool polyline in new[] { false, true }) foreach (int payload in new[] { 0, 1, 2 }) {
            int p = payload;
            Run($"hatch-graphics/hydration/{version}/{binary}/{polyline}/{p}", () => {
                var doc = new DxfDocument(version); var hatch = HatchGraphicsSubject(polyline, true); doc.Entities.Add(hatch);
                byte[]? bytes = p == 0 ? null : p == 1 ? Array.Empty<byte>() : HatchGraphicsBytes;
                hatch.ProxyGraphics = bytes;
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Associative seed save"); stream.Position = 0;
                var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Associative hydration failed");
                var copy = loaded.Entities.Hatches.Single(); Check(copy.Associative, "Hydration lost associativity");
                Check(HatchGraphicsSameProxy(bytes, copy.ProxyGraphics), "Hydration discarded stored proxy state");
                Equal(polyline ? 1 : 4, copy.BoundaryPaths[0].Entities.Count, "Hydration source count");
                foreach (var source in copy.BoundaryPaths[0].Entities) Check(source.Reactors.Contains(copy), "Missing hydrated backlink");
                Equal(0, loaded.Objects.Validate().Count, "Hydrated graph");
                copy.Elevation += 1; Check(copy.ProxyGraphics == null, "Hydration left subsequent edits unable to invalidate cache");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (bool polyline in new[] { false, true }) for (int placement = 0; placement < 4; placement++) {
            int p = placement;
            Run($"hatch-graphics/wire/{version}/{binary}/{polyline}/{p}", () => HatchGraphicsWire(version, binary, polyline, p));
        }
    }

    private sealed class HatchGraphicsDispatchProbe : Hatch
    {
        internal int Calls;
        internal HatchGraphicsDispatchProbe() : base(HatchPattern.Solid, false) { }
        public override void TransformBy(Matrix3 matrix, Vector3 translation) { Calls++; }
    }

    private static bool HatchGraphicsSameProxy(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);

    private static void HatchGraphicsReject(Hatch hatch, Action operation)
    {
        var doc = new DxfDocument(); doc.Entities.Add(hatch); hatch.ProxyGraphics = HatchGraphicsBytes;
        var pattern = hatch.Pattern; var paths = hatch.BoundaryPaths.ToArray(); var edges = paths.SelectMany(p => p.Edges).ToArray();
        var sources = paths.SelectMany(p => p.Entities).ToArray(); var reactors = sources.Select(s => s.Reactors.ToArray()).ToArray();
        var seeds = hatch.SeedPoints.ToArray(); var normal = hatch.Normal; double elevation = hatch.Elevation, scale = pattern.Scale, angle = pattern.Angle;
        bool associative = hatch.Associative; var owner = hatch.Owner; string handle = hatch.Handle; long handleSeed = OwnershipSeed(doc); int events = 0;
        hatch.HatchBoundaryPathAdded += (_, _) => events++; hatch.HatchBoundaryPathRemoved += (_, _) => events++;
        bool rejected = false;
        try { operation(); } catch (ArgumentException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid hatch operation accepted"); Equal(0, events, "Rejected operation emitted path events");
        Check(paths.SequenceEqual(hatch.BoundaryPaths) && edges.SequenceEqual(hatch.BoundaryPaths.SelectMany(p => p.Edges)), "Rejected operation changed path identity");
        Check(sources.SequenceEqual(paths.SelectMany(p => p.Entities)), "Rejected operation unlinked source");
        for (int i = 0; i < sources.Length; i++) Check(reactors[i].SequenceEqual(sources[i].Reactors), "Rejected operation changed reactors");
        Check(seeds.SequenceEqual(hatch.SeedPoints) && ReferenceEquals(pattern, hatch.Pattern), "Rejected operation changed pattern/seeds");
        RawLinePointBits(normal, hatch.Normal); SameDoubleBits(elevation, hatch.Elevation, "Rejected elevation");
        SameDoubleBits(scale, pattern.Scale, "Rejected pattern scale"); SameDoubleBits(angle, pattern.Angle, "Rejected pattern angle");
        Check(HatchGraphicsSameProxy(HatchGraphicsBytes, hatch.ProxyGraphics), "Rejected operation cleared graphics");
        Equal(associative, hatch.Associative, "Rejected association"); Equal(handle, hatch.Handle, "Rejected handle");
        Equal(handleSeed, OwnershipSeed(doc), "Rejected operation allocated handles"); Check(ReferenceEquals(owner, hatch.Owner), "Rejected ownership");
    }

    private static void HatchGraphicsWire(DxfVersion version, bool binary, bool polyline, int placement)
    {
        var doc = new DxfDocument(version); doc.Comments.Clear();
        var hosts = Enumerable.Range(0, HatchGraphicsEdits.Length).Select(edit => {
            var hatch = HatchGraphicsSubject(polyline); hatch.Layer = new Layer($"HG_{edit:D2}");
            if (edit == 11) hatch.BoundaryPaths.Add(HatchGraphicsPath(polyline, 10));
            return hatch;
        }).ToArray();
        if (placement == 0) foreach (var h in hosts) doc.Entities.Add(h);
        else if (placement == 1) {
            doc.Layouts.Add(new Layout("HG_PAPER")); foreach (var h in hosts) doc.Layouts["HG_PAPER"].AssociatedBlock.Entities.Add(h);
        } else {
            var block = new Block("HG_HOLDER", hosts); if (placement == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
        }
        for (int e = 0; e < hosts.Length; e++) { hosts[e].ProxyGraphics = HatchGraphicsBytes; HatchGraphicsEdit(hosts[e], e, polyline); }
        doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
        string[] handles = hosts.Select(h => h.Handle).ToArray();
        void CheckDocument(DxfDocument document) {
            var loaded = document.Blocks.SelectMany(b => b.Entities).OfType<Hatch>().OrderBy(h => h.Layer.Name, StringComparer.Ordinal).ToArray();
            Equal(hosts.Length, loaded.Length, "Stored hatch count");
            for (int i = 0; i < loaded.Length; i++) {
                Equal(handles[i], loaded[i].Handle, "Stored hatch identity");
                Check(HatchGraphicsSameProxy(i < 5 ? HatchGraphicsBytes : null, loaded[i].ProxyGraphics), "Stored proxy state");
                Equal(hosts[i].Pattern.Name, loaded[i].Pattern.Name, "Stored pattern"); SameDoubleBits(hosts[i].Elevation, loaded[i].Elevation, "Stored elevation");
                Equal(hosts[i].BoundaryPaths.Count, loaded[i].BoundaryPaths.Count, "Stored topology");
                Equal("unchanged", (string)loaded[i].XData["HATCH_GRAPHICS_KEEP"].XDataRecord.Single().Value, "Stored unrelated XData");
                var clone = (Hatch)loaded[i].Clone(); Check(HatchGraphicsSameProxy(loaded[i].ProxyGraphics, clone.ProxyGraphics), "Stored clone proxy");
                clone.Elevation += 1; Check(clone.ProxyGraphics == null, "Clone elevation cache");
                Check(HatchGraphicsSameProxy(i < 5 ? HatchGraphicsBytes : null, loaded[i].ProxyGraphics), "Clone changed source cache");
            }
            Equal(0, document.Objects.Validate().Count, "Stored hatch graph");
            var line = document.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
        }
        CheckDocument(doc);
        string stem = $"hatch-graphics-{version}-{binary}-{polyline}-{placement}";
        using var source = new MemoryStream(); Check(doc.Save(source, binary), "Hatch source save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray()); source.Position = 0;
        var first = DxfDocument.Load(source) ?? throw new InvalidOperationException("Hatch source load"); CheckDocument(first);
        foreach (bool output in new[] { false, true }) {
            using var stream = new MemoryStream(); Check(first.Save(stream, output), "Hatch resave");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray()); stream.Position = 0;
            CheckDocument(DxfDocument.Load(stream) ?? throw new InvalidOperationException("Hatch reload"));
        }
        Check(source.CanRead, "Hatch load closed caller stream");
    }
}
