// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly byte[] UnderlayProxy = Enumerable.Range(0, 300).Select(i => (byte)(i * 31)).ToArray();
    private static Vector3 UnderlayNormal(int plane) => plane == 0 ? Vector3.UnitZ : plane == 1 ? Vector3.UnitY : new Vector3(0, 3, 4);
    private static Vector3 UnderlayShift(int map) => map == 0 ? Vector3.Zero : new Vector3(11, -13, 17);
    private static Matrix3 UnderlayMap(int plane, int map)
    {
        switch (map)
        {
            case 0: case 1: return Matrix3.Identity;
            case 2: return Matrix3.Scale(2, 3, 4);
            case 3: return new Matrix3(0, 1, 0, 1, 0, 0, 0, 0, 1);
            case 4: return new Matrix3(0, -1, 0, 1, 0, 0, 0, 0, 1);
            case 5: return Matrix3.Scale(-2);
            case 6:
                var axes = MathHelper.ArbitraryAxis(Vector3.Normalize(UnderlayNormal(plane)));
                return axes * new Matrix3(1, 0, 0, 0, 1, 0, .5, 0, 1) * axes.Transpose();
            case 7: return Matrix3.Scale(1e-5);
            default: throw new ArgumentOutOfRangeException(nameof(map));
        }
    }
    private static Underlay UnderlaySubject(int type, int plane, int map)
    {
        UnderlayDefinition definition = type == 0 ? new UnderlayPdfDefinition("UA_PDF", "underlay.pdf")
            : type == 1 ? new UnderlayDwfDefinition("UA_DWF", "underlay.dwf")
            : new UnderlayDgnDefinition("UA_DGN", "underlay.dgn");
        var item = new Underlay(definition, new Vector3(5, -3, 7), 1)
        {
            Normal = UnderlayNormal(plane), Scale = new Vector2(4, 3),
            Rotation = map == 2 || map == 6 ? 0 : 37,
            Contrast = 61, Fade = 17, DisplayOptions = (UnderlayDisplayFlags)11,
            ClippingBoundary = new ClippingBoundary(new[] { new Vector2(.25, .5), new Vector2(6, .25), new Vector2(1, 5) })
        };
        var data = new XData(new ApplicationRegistry("UNDERLAY_AFFINE_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); item.XData.Add(data);
        return item;
    }
    private static Vector3[] UnderlayAxes(Underlay item)
    {
        var axes = MathHelper.ArbitraryAxis(item.Normal); double angle = item.Rotation * MathHelper.DegToRad;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new[] { item.Position, axes * new Vector3(c * item.Scale.X, s * item.Scale.X, 0),
            axes * new Vector3(-s * item.Scale.Y, c * item.Scale.Y, 0) };
    }
    private static Vector3[] UnderlayExpected(int plane, int map)
    {
        var original = UnderlayAxes(UnderlaySubject(0, plane, map)); var matrix = UnderlayMap(plane, map);
        return new[] { matrix * original[0] + UnderlayShift(map), matrix * original[1], matrix * original[2] };
    }
    private static void UnderlayCheckAxes(Vector3[] expected, Underlay item)
    {
        var actual = UnderlayAxes(item);
        for (int i = 0; i < 3; i++) ImageNearVector(expected[i], actual[i], "Underlay WCS insertion/basis " + i);
    }
    private static long[] UnderlayBits(Underlay item) => new[] { item.Position.X, item.Position.Y, item.Position.Z,
        item.Normal.X, item.Normal.Y, item.Normal.Z, item.Scale.X, item.Scale.Y, item.Rotation }
        .Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void UnderlayApply(Underlay item, Matrix3 matrix, Vector3 shift, bool matrix4)
    {
        if (matrix4) item.TransformBy(LineReviewMatrix4(matrix, shift)); else item.TransformBy(matrix, shift);
    }
    private static void UnderlayReject(Underlay item, Action action, byte[]? proxy)
    {
        item.ProxyGraphics = proxy; var bits = UnderlayBits(item); var clip = item.ClippingBoundary;
        var definition = item.Definition; var owner = item.Owner; string handle = item.Handle;
        bool rejected = false;
        try { action(); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException) { rejected = true; }
        Check(rejected, "Unrepresentable underlay transform accepted");
        Check(bits.SequenceEqual(UnderlayBits(item)) && ReferenceEquals(clip, item.ClippingBoundary)
            && ReferenceEquals(definition, item.Definition) && ReferenceEquals(owner, item.Owner) && handle == item.Handle,
            "Rejected underlay transform changed stored state");
        Check(ImageProxyEqual(proxy, item.ProxyGraphics), "Rejected underlay transform changed proxy");
    }
    private static void RegisterUnderlayAffineTests()
    {
        for (int type = 0; type < 3; type++) for (int plane = 0; plane < 3; plane++)
        for (int map = 0; map < 8; map++) foreach (bool matrix4 in new[] { false, true })
        {
            int t = type, p = plane, m = map;
            Run($"underlay-affine/api/{t}/{p}/{m}/{matrix4}", () => {
                var item = UnderlaySubject(t, p, m); var doc = new DxfDocument(); doc.Entities.Add(item);
                item.ProxyGraphics = UnderlayProxy; var bits = UnderlayBits(item); var clip = item.ClippingBoundary;
                var definition = item.Definition; var owner = item.Owner; string handle = item.Handle;
                UnderlayApply(item, UnderlayMap(p, m), UnderlayShift(m), matrix4);
                UnderlayCheckAxes(UnderlayExpected(p, m), item);
                if (m == 0) Check(bits.SequenceEqual(UnderlayBits(item)), "Identity changed underlay bits");
                Check(ImageProxyEqual(m == 0 ? UnderlayProxy : null, item.ProxyGraphics), "Underlay transform cache");
                Check(ReferenceEquals(clip, item.ClippingBoundary) && ReferenceEquals(definition, item.Definition)
                    && ReferenceEquals(owner, item.Owner) && handle == item.Handle, "Transform changed underlay dependencies/identity");
                Check(item.Scale.X > 0 && item.Scale.Y > 0, "Transformed positive source lost canonical scales");
                var clone = (Underlay)item.Clone(); UnderlayCheckAxes(UnderlayExpected(p, m), clone);
                Check(!ReferenceEquals(clip, clone.ClippingBoundary) && !ReferenceEquals(definition, clone.Definition), "Clone aliases dependencies");
                clone.Scale *= 2; UnderlayCheckAxes(UnderlayExpected(p, m), item);
                Equal(0, doc.Objects.Validate().Count, "Underlay graph");
            });
        }
        for (int signs = 0; signs < 4; signs++) for (int plane = 0; plane < 3; plane++)
        foreach (bool matrix4 in new[] { false, true })
        {
            int s = signs, p = plane;
            Run($"underlay-affine/signed-source/{s}/{p}/{matrix4}", () => {
                var item = UnderlaySubject(0, p, 3); item.Scale = new Vector2((s & 1) == 0 ? 4 : -4, (s & 2) == 0 ? 3 : -3);
                var before = UnderlayAxes(item); var matrix = UnderlayMap(p, 3);
                var expected = new[] { matrix * before[0] + UnderlayShift(3), matrix * before[1], matrix * before[2] };
                UnderlayApply(item, matrix, UnderlayShift(3), matrix4); UnderlayCheckAxes(expected, item);
                Check(item.Scale.X > 0 && item.Scale.Y > 0, "Signed source did not canonicalize after linear transform");
                using var stream = new MemoryStream(); var doc = new DxfDocument(); doc.Entities.Add(item);
                Check(doc.Save(stream, matrix4), "Canonical signed-source save"); stream.Position = 0;
                var copy = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Canonical signed-source load");
                UnderlayCheckAxes(expected, copy.Blocks.SelectMany(b => b.Entities).OfType<Underlay>().Single());
            });
        }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            double value = bad;
            for (int component = 0; component < 28; component++)
            {
                int c = component;
                Run($"underlay-affine/nonfinite/{c}/{ParameterBits(value)}", () => {
                    var item = UnderlaySubject(0, 0, 0);
                    if (c < 16) { var matrix = Matrix4.Identity; matrix[c / 4, c % 4] = value;
                        UnderlayReject(item, () => item.TransformBy(matrix), UnderlayProxy); }
                    else { var matrix = Matrix3.Identity; var shift = Vector3.Zero; int k = c - 16;
                        if (k < 9) matrix[k / 3, k % 3] = value;
                        else shift = k == 9 ? new Vector3(value, 0, 0) : k == 10 ? new Vector3(0, value, 0) : new Vector3(0, 0, value);
                        UnderlayReject(item, () => item.TransformBy(matrix, shift), UnderlayProxy); }
                });
            }
        }
        for (int column = 0; column < 4; column++)
        {
            int c = column;
            Run($"underlay-affine/projective/{c}", () => {
                var item = UnderlaySubject(0, 0, 0); var matrix = Matrix4.Identity; matrix[3, c] = c == 3 ? 2 : double.Epsilon;
                UnderlayReject(item, () => item.TransformBy(matrix), Array.Empty<byte>());
            });
        }
        for (int kind = 0; kind < 6; kind++) for (int state = 0; state < 3; state++)
        foreach (bool matrix4 in new[] { false, true })
        {
            int k = kind, s = state;
            Run($"underlay-affine/refusal/{k}/{s}/{matrix4}", () => {
                var item = UnderlaySubject(0, 0, k == 0 ? 0 : 2);
                var matrix = k == 0 ? Matrix3.Scale(2, 3, 4)
                    : k == 1 ? new Matrix3(1, .5, 0, 0, 1, 0, 0, 0, 1)
                    : k == 2 ? Matrix3.Scale(0, 1, 1)
                    : k == 3 ? Matrix3.Scale(1e-100)
                    : k == 4 ? Matrix3.Scale(double.MaxValue)
                    : new Matrix3(1, 1, 0, 0, 1e-16, 0, 0, 0, 1);
                UnderlayReject(item, () => UnderlayApply(item, matrix, new Vector3(7, 8, 9), matrix4),
                    s == 0 ? null : s == 1 ? Array.Empty<byte>() : UnderlayProxy);
            });
        }
        for (int property = 0; property < 7; property++) for (int state = 0; state < 3; state++)
        foreach (bool changed in new[] { false, true })
        {
            int p = property, s = state;
            Run($"underlay-affine/edit/{p}/{s}/{changed}", () => {
                var item = UnderlaySubject(0, 0, 0); byte[]? proxy = s == 0 ? null : s == 1 ? Array.Empty<byte>() : UnderlayProxy;
                item.ProxyGraphics = proxy; var clone = (Underlay)item.Clone(); var bits = UnderlayBits(item);
                switch (p) {
                    case 0: item.Position = changed ? new Vector3(1, 2, 3) : item.Position; break;
                    case 1: item.Scale = changed ? new Vector2(2, 5) : item.Scale; break;
                    case 2: item.Rotation = changed ? 90 : item.Rotation; break;
                    case 3: item.Contrast = changed ? (short)20 : item.Contrast; break;
                    case 4: item.Fade = changed ? (short)80 : item.Fade; break;
                    case 5: item.DisplayOptions = changed ? (UnderlayDisplayFlags)0 : item.DisplayOptions; break;
                    case 6: item.ClippingBoundary = changed ? null : item.ClippingBoundary; break;
                }
                Check(ImageProxyEqual(changed ? null : proxy, item.ProxyGraphics), "Underlay edit cache");
                Check(bits.SequenceEqual(UnderlayBits(clone)) && ImageProxyEqual(proxy, clone.ProxyGraphics), "Edit changed clone");
            });
        }
        for (int kind = 0; kind < 6; kind++) for (int state = 0; state < 3; state++)
        {
            int k = kind, s = state;
            Run($"underlay-affine/edit-refusal/{k}/{s}", () => {
                var item = UnderlaySubject(0, 0, 0); byte[]? proxy = s == 0 ? null : s == 1 ? Array.Empty<byte>() : UnderlayProxy;
                UnderlayReject(item, () => {
                    switch (k) {
                        case 0: item.Contrast = 19; break; case 1: item.Contrast = 101; break;
                        case 2: item.Fade = -1; break; case 3: item.Fade = 81; break;
                        case 4: item.Scale = new Vector2(0, 1); break; case 5: item.Scale = new Vector2(1, 0); break;
                    }
                }, proxy);
                Equal((short)61, item.Contrast, "Refused contrast changed"); Equal((short)17, item.Fade, "Refused fade changed");
            });
        }
        Run("underlay-affine/normal-callback", () => {
            var item = new UnderlayNormalTrap(); item.TransformBy(Matrix3.Scale(2), new Vector3(1, 2, 3));
            Equal(0, item.Calls, "Underlay transform invoked virtual Normal");
        });
        Run("underlay-affine/transform-callback", () => {
            var item = new UnderlayTransformTrap(); var matrix = Matrix4.Identity; matrix.M44 = 2;
            Throws<NotSupportedException>(() => item.TransformBy(matrix)); Equal(0, item.Calls, "Projective input reached virtual affine callback");
            item.TransformBy(Matrix4.Identity); Equal(1, item.Calls, "Valid Matrix4 lost virtual affine dispatch");
        });
        Run("underlay-affine/large-offset-and-scale", () => {
            var item = UnderlaySubject(0, 0, 2); item.TransformBy(Matrix3.Scale(2, 3, 4), new Vector3(1e100, -1e100, 1e100));
            SameDoubleBits(8, item.Scale.X, "Large-offset X scale"); SameDoubleBits(9, item.Scale.Y, "Large-offset Y scale");
            var huge = UnderlaySubject(0, 0, 2); huge.TransformBy(Matrix3.Scale(1e100), Vector3.Zero);
            UnderlayCheckAxes(new[] { new Vector3(5e100, -3e100, 7e100), new Vector3(4e100, 0, 0), new Vector3(0, 3e100, 0) }, huge);
        });
        Run("underlay-affine/plane-preserving-singular-map", () => {
            var item = UnderlaySubject(0, 0, 2); item.TransformBy(Matrix3.Scale(2, 3, 0), Vector3.Zero);
            UnderlayCheckAxes(new[] { new Vector3(10, -9, 0), new Vector3(8, 0, 0), new Vector3(0, 9, 0) }, item);
        });
        Run("underlay-affine/signed-translation-noop", () => {
            var item = UnderlaySubject(0, 2, 0); item.Scale = new Vector2(-4, 3); var bits = UnderlayBits(item);
            item.ProxyGraphics = Array.Empty<byte>(); item.TransformBy(Matrix4.Identity);
            Check(bits.SequenceEqual(UnderlayBits(item)) && item.ProxyGraphics != null, "Signed identity changed representation");
            item.TransformBy(Matrix3.Identity, new Vector3(1, 2, 3));
            SameDoubleBits(-4, item.Scale.X, "Translation changed signed X"); SameDoubleBits(3, item.Scale.Y, "Translation changed signed Y");
            Check(bits.Skip(3).SequenceEqual(UnderlayBits(item).Skip(3)), "Translation changed orientation");
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 4; placement++)
        {
            int p = placement;
            Run($"underlay-affine/wire/{version}/{binary}/{p}", () => UnderlayWire(version, binary, p));
        }
    }
    private static void UnderlayWire(DxfVersion version, bool binary, int placement)
    {
        var doc = new DxfDocument(version); doc.Comments.Clear(); var items = new List<Underlay>();
        for (int type = 0; type < 3; type++) for (int plane = 0; plane < 3; plane++) for (int map = 0; map < 8; map++)
        {
            var item = UnderlaySubject(type, plane, map); item.Layer = new Layer($"UA_{type}_{plane}_{map}");
            item.ProxyGraphics = UnderlayProxy; UnderlayApply(item, UnderlayMap(plane, map), UnderlayShift(map), (map & 1) != 0);
            UnderlayCheckAxes(UnderlayExpected(plane, map), item); items.Add(item);
        }
        if (placement == 0) doc.Entities.Add(items);
        else if (placement == 1) { doc.Layouts.Add(new Layout("UA_PAPER")); foreach (var item in items) doc.Layouts["UA_PAPER"].AssociatedBlock.Entities.Add(item); }
        else { var block = new Block("UA_HOLDER", items); if (placement == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block); }
        doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
        string[] handles = items.Select(i => i.Handle).ToArray(); string stem = $"underlay-affine-{version}-{binary}-{placement}";
        using var source = new MemoryStream(); Check(doc.Save(source, binary), "Underlay source save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source.ToArray()); source.Position = 0;
        var loaded = DxfDocument.Load(source) ?? throw new InvalidOperationException("Underlay load"); UnderlayCheckDocument(loaded, handles);
        foreach (bool output in new[] { false, true }) {
            using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Underlay resave");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray()); stream.Position = 0;
            UnderlayCheckDocument(DxfDocument.Load(stream) ?? throw new InvalidOperationException("Underlay reload"), handles);
        }
        var first = loaded.Blocks.SelectMany(b => b.Entities).OfType<Underlay>().Single(i => i.Layer.Name == "UA_0_0_0");
        Check(ImageProxyEqual(UnderlayProxy, first.ProxyGraphics), "Hydration lost input graphics");
        first.Fade++; Check(first.ProxyGraphics == null, "Post-load edit retained input graphics");
        Check(source.CanRead, "Underlay load closed caller stream");
    }
    private static void UnderlayCheckDocument(DxfDocument doc, string[] handles)
    {
        var items = doc.Blocks.SelectMany(b => b.Entities).OfType<Underlay>().OrderBy(i => i.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(72, items.Length, "Underlay wire inventory");
        for (int i = 0; i < items.Length; i++) {
            var item = items[i]; var parts = item.Layer.Name.Split('_'); int type = int.Parse(parts[1]), plane = int.Parse(parts[2]), map = int.Parse(parts[3]);
            UnderlayCheckAxes(UnderlayExpected(plane, map), item); Equal(handles[i], item.Handle, "Underlay handle");
            Check(ImageProxyEqual(map == 0 ? UnderlayProxy : null, item.ProxyGraphics), "Underlay wire graphics");
            Equal(type == 0 ? UnderlayType.PDF : type == 1 ? UnderlayType.DWF : UnderlayType.DGN, item.Definition.Type, "Underlay definition type");
            Equal(type == 0 ? "UA_PDF" : type == 1 ? "UA_DWF" : "UA_DGN", item.Definition.Name, "Underlay definition name");
            var expectedClip = UnderlaySubject(type, plane, map).ClippingBoundary;
            Equal(expectedClip.Vertexes.Count, item.ClippingBoundary.Vertexes.Count, "Underlay clip count");
            for (int j = 0; j < expectedClip.Vertexes.Count; j++) {
                SameDoubleBits(expectedClip.Vertexes[j].X, item.ClippingBoundary.Vertexes[j].X, "Underlay clip X");
                SameDoubleBits(expectedClip.Vertexes[j].Y, item.ClippingBoundary.Vertexes[j].Y, "Underlay clip Y");
            }
            Equal((short)61, item.Contrast, "Underlay contrast"); Equal((short)17, item.Fade, "Underlay fade");
            Equal((UnderlayDisplayFlags)11, item.DisplayOptions, "Underlay flags");
            Equal("unchanged", (string)item.XData["UNDERLAY_AFFINE_KEEP"].XDataRecord.Single().Value, "Underlay XData");
            UnderlayCheckAxes(UnderlayExpected(plane, map), (Underlay)item.Clone());
        }
        Equal(0, doc.Objects.Validate().Count, "Underlay wire graph");
        var line = doc.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint); RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
    private sealed class UnderlayNormalTrap : Underlay
    {
        public int Calls;
        public UnderlayNormalTrap() : base(new UnderlayPdfDefinition("trap.pdf")) { }
        public override Vector3 Normal { get { Calls++; throw new InvalidOperationException("Unexpected underlay normal getter"); }
            set { Calls++; throw new InvalidOperationException("Unexpected underlay normal setter"); } }
    }
    private sealed class UnderlayTransformTrap : Underlay
    {
        public int Calls;
        public UnderlayTransformTrap() : base(new UnderlayPdfDefinition("trap.pdf")) { }
        public override void TransformBy(Matrix3 matrix, Vector3 shift) { Calls++; }
    }
}
