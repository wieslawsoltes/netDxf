// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private const int UnderlayScaleVariants = 19;
    private static double UnderlayNegativeZero => BitConverter.Int64BitsToDouble(long.MinValue);
    private static Vector3 UnderlayScaleValue(int variant)
    {
        if (variant < 8) return new Vector3((variant & 1) == 0 ? 2 : -2,
            (variant & 2) == 0 ? 3 : -3, (variant & 4) == 0 ? 4 : -4);
        return variant switch {
            8 => new Vector3(1),
            9 => new Vector3(1e-100, -1e-100, -1e-200),
            10 => new Vector3(double.Epsilon, -double.Epsilon, double.Epsilon),
            11 => new Vector3(double.MaxValue, -double.MaxValue, -double.MaxValue),
            12 => new Vector3(0.0, UnderlayNegativeZero, 0.0),
            13 => new Vector3(UnderlayNegativeZero, 0.0, UnderlayNegativeZero),
            14 => new Vector3(BitConverter.Int64BitsToDouble(0x0010000000000000),
                -BitConverter.Int64BitsToDouble(0x0010000000000000), -BitConverter.Int64BitsToDouble(0x0010000000000000)),
            15 => new Vector3(Math.BitIncrement(1), Math.BitDecrement(1), -.125),
            16 => new Vector3(1, -3, -4),
            17 => new Vector3(-2, 1, 4),
            18 => new Vector3(-2, -3, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
    }
    private static bool UnderlayScaleOmitted(int variant, short code)
        => variant == 8 || (variant == 16 && code == 41) || (variant == 17 && code == 42) || (variant == 18 && code == 43);
    private static Vector3 UnderlayStoredScale(Underlay item) => new Vector3(item.Scale.X, item.Scale.Y, item.ScaleZ);
    private static Underlay UnderlayScaleSubject(int type, int plane, int variant)
    {
        var item = UnderlaySubject(type, plane, 0);
        item.Layer = new Layer($"US_{type}_{plane}_{variant:D2}");
        item.ProxyGraphics = UnderlayProxy;
        var data = new XData(new ApplicationRegistry("UNDERLAY_SCALE_DATA"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.Real, -9.25));
        data.XDataRecord.Add(new XDataRecord(XDataCode.Distance, 123.5));
        data.XDataRecord.Add(new XDataRecord(XDataCode.ScaleFactor, -17.25));
        item.XData.Add(data);
        return item;
    }
    private static bool IsUnderlayRecord(DxfRawRecord record)
        => record.Name == "PDFUNDERLAY" || record.Name == "DWFUNDERLAY" || record.Name == "DGNUNDERLAY";
    private static byte[] UnderlayScaleInput(DxfDocument doc, bool binary)
    {
        using var seed = new MemoryStream(); Check(doc.Save(seed, binary), "Underlay scale seed save");
        var raw = LoadRaw(seed.ToArray()); var replacements = new Dictionary<int, DxfTag?>();
        foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(IsUnderlayRecord))
        {
            int variant = int.Parse(((string)record.Tags.Single(t => t.Code == 8).Value).Split('_')[3]);
            var scale = UnderlayScaleValue(variant);
            for (int i = 0; i < record.Tags.Count; i++)
            {
                short code = record.Tags[i].Code;
                if (code < 41 || code > 43) continue;
                replacements.Add(record.StartTagIndex + i, UnderlayScaleOmitted(variant, code) ? null :
                    new DxfTag(code, code == 41 ? scale.X : code == 42 ? scale.Y : scale.Z));
            }
        }
        var tags = raw.Tags.Select((t, i) => replacements.TryGetValue(i, out var replacement) ? replacement : t)
            .Where(t => t != null).Select(t => t!).ToArray();
        // The fixture transport is independently authored, not the typed writer's
        // scalar output. All unselected document tags come from a valid seed graph.
        return RawFixtureBytes(tags, binary);
    }
    private static void UnderlayScaleCheckDocument(DxfDocument doc, Dictionary<string, string> handles)
    {
        var items = doc.Blocks.SelectMany(b => b.Entities).OfType<Underlay>().ToArray();
        Equal(3 * 3 * UnderlayScaleVariants, items.Length, "Underlay scalar inventory");
        foreach (var item in items)
        {
            string[] parts = item.Layer.Name.Split('_'); int type = int.Parse(parts[1]);
            int plane = int.Parse(parts[2]), variant = int.Parse(parts[3]);
            var expected = UnderlayScaleValue(variant); RawLinePointBits(expected, UnderlayStoredScale(item));
            Equal(handles[item.Layer.Name], item.Handle, "Underlay scalar handle");
            Check(ImageProxyEqual(UnderlayProxy, item.ProxyGraphics), "Scalar hydration lost graphics");
            Equal(type == 0 ? UnderlayType.PDF : type == 1 ? UnderlayType.DWF : UnderlayType.DGN,
                item.Definition.Type, "Scalar definition type");
            var seed = UnderlayScaleSubject(type, plane, variant);
            ImageNearVector(seed.Position, item.Position, "Scalar insertion");
            ImageNearVector(seed.Normal, item.Normal, "Scalar normal");
            SameDoubleBits(seed.Rotation, item.Rotation, "Scalar rotation");
            Equal(seed.DisplayOptions, item.DisplayOptions, "Scalar flags");
            Equal(seed.Contrast, item.Contrast, "Scalar contrast"); Equal(seed.Fade, item.Fade, "Scalar fade");
            Check(seed.ClippingBoundary.Vertexes.SequenceEqual(item.ClippingBoundary.Vertexes), "Scalar clipping");
            var data = item.XData["UNDERLAY_SCALE_DATA"].XDataRecord;
            Equal(3, data.Count, "Scalar XData count");
            SameDoubleBits(-9.25, (double)data[0].Value, "Scalar XData real");
            SameDoubleBits(123.5, (double)data[1].Value, "Scalar XData distance");
            SameDoubleBits(-17.25, (double)data[2].Value, "Scalar XData scale");
            var clone = (Underlay)item.Clone(); RawLinePointBits(expected, UnderlayStoredScale(clone));
            Check(ImageProxyEqual(UnderlayProxy, clone.ProxyGraphics), "Scalar clone lost graphics");
            Check(!ReferenceEquals(item.Definition, clone.Definition)
                && !ReferenceEquals(item.ClippingBoundary, clone.ClippingBoundary), "Scalar clone dependency alias");
            clone.TransformBy(Matrix4.Identity); RawLinePointBits(expected, UnderlayStoredScale(clone));
            Check(ImageProxyEqual(UnderlayProxy, clone.ProxyGraphics), "Scalar identity lost graphics");
            clone.TransformBy(Matrix3.Identity, new Vector3(7, 11, -13));
            RawLinePointBits(expected, UnderlayStoredScale(clone)); Check(clone.ProxyGraphics == null, "Scalar translation cache");
            if (variant == 12 || variant == 13)
                UnderlayReject(clone, () => clone.TransformBy(Matrix3.Scale(2), Vector3.Zero), UnderlayProxy);
            clone.Scale = new Vector2(-5, 7); clone.ScaleZ = -9;
            RawLinePointBits(expected, UnderlayStoredScale(item));
        }
        var line = doc.Entities.Lines.Single(); RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
        Equal(0, doc.Objects.Validate().Count, "Underlay scalar graph");
    }
    private static void RegisterUnderlayScaleTests()
    {
        Run("underlay-scale/text-zero-spellings", () => {
            foreach (short code in ExpectedTagTypes().Where(t => t.Value == DxfTagValueType.Double).Select(t => t.Key))
            foreach (string token in new[] { "-0", "-0.0", "-00.000e+23", " \t-0E-23\t ", "0", "+0.0", " \t000E+23 ", "1", "-1" })
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{code}\n{token}\n0\nEOF\n"));
                object reader = NewCodeReader(stream, false); Invoke(reader, "Next");
                Equal(code, TagCode(reader), "Signed-zero group boundary");
                double expected = token == "1" ? 1 : token == "-1" ? -1 : token.TrimStart().StartsWith("-", StringComparison.Ordinal) ? UnderlayNegativeZero : 0;
                SameDoubleBits(expected, (double)Invoke(reader, "ReadDouble")!, "Signed-zero text spelling");
                Invoke(reader, "Next"); Equal("EOF", (string)Invoke(reader, "ReadString")!, "Signed-zero following record");
            }
            foreach (string token in new[] { "--0", "-0suffix", "-0\0", "-Infinity", "NaN" })
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"41\n{token}\n0\nEOF\n"));
                object reader = NewCodeReader(stream, false); Throws<FormatException>(() => Invoke(reader, "Next"));
            }
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 4; placement++)
        {
            int p = placement;
            Run($"underlay-scale/wire/{version}/{binary}/{p}", () => {
                var doc = new DxfDocument(version); doc.Comments.Clear(); var items = new List<Underlay>();
                for (int type = 0; type < 3; type++) for (int plane = 0; plane < 3; plane++)
                for (int variant = 0; variant < UnderlayScaleVariants; variant++) items.Add(UnderlayScaleSubject(type, plane, variant));
                if (p == 0) doc.Entities.Add(items);
                else if (p == 1) { doc.Layouts.Add(new Layout("US_PAPER")); foreach (var item in items) doc.Layouts["US_PAPER"].AssociatedBlock.Entities.Add(item); }
                else { var block = new Block("US_HOLDER", items); if (p == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block); }
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                byte[] input = UnderlayScaleInput(doc, binary), original = (byte[])input.Clone();
                var handles = items.ToDictionary(i => i.Layer.Name, i => i.Handle);
                string stem = $"underlay-scale-{version}-{binary}-{p}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), input);
                using var stream = new MemoryStream(input);
                var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Underlay scalar input load");
                UnderlayScaleCheckDocument(loaded, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var result = new MemoryStream(); Check(loaded.Save(result, output), "Underlay scalar resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), result.ToArray()); result.Position = 0;
                    var copy = DxfDocument.Load(result) ?? throw new InvalidOperationException("Underlay scalar reload");
                    UnderlayScaleCheckDocument(copy, handles);
                    using var again = new MemoryStream(); Check(copy.Save(again, output), "Underlay scalar second resave"); again.Position = 0;
                    UnderlayScaleCheckDocument(DxfDocument.Load(again) ?? throw new InvalidOperationException("Underlay scalar second reload"), handles);
                }
                Check(stream.CanRead && input.SequenceEqual(original), "Underlay scalar input buffer/stream changed");
            });
        }
        foreach (double value in new[] { double.Epsilon, -double.Epsilon, 1e-100, -1e-100, 2.0, -2.0, double.MaxValue, -double.MaxValue })
        for (int state = 0; state < 3; state++)
        {
            int s = state; double v = value;
            Run($"underlay-scale/api/{ParameterBits(v)}/{s}", () => {
                var item = UnderlayScaleSubject(0, 0, 0); byte[]? proxy = s == 0 ? null : s == 1 ? Array.Empty<byte>() : UnderlayProxy;
                SameDoubleBits(1, item.ScaleZ, "Default scale Z"); item.ProxyGraphics = proxy;
                item.Scale = new Vector2(v, -v); Check(item.ProxyGraphics == null, "Scale edit cache");
                item.ProxyGraphics = proxy; item.ScaleZ = v;
                Check(item.ProxyGraphics == null, "Scale Z edit cache"); item.ProxyGraphics = proxy;
                item.Scale = item.Scale; item.ScaleZ = item.ScaleZ;
                Check(ImageProxyEqual(proxy, item.ProxyGraphics), "Scale no-op cache");
                RawLinePointBits(new Vector3(v, -v, v), UnderlayStoredScale((Underlay)item.Clone()));
                using var stream = new MemoryStream(); var doc = new DxfDocument(); doc.Entities.Add(item);
                Check(doc.Save(stream, s == 1), "API scale save"); stream.Position = 0;
                var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("API scale load");
                RawLinePointBits(new Vector3(v, -v, v), UnderlayStoredScale(loaded.Blocks.SelectMany(b => b.Entities).OfType<Underlay>().Single()));
            });
        }
        foreach (double value in new[] { 0.0, UnderlayNegativeZero, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        for (int field = 0; field < 3; field++) for (int state = 0; state < 3; state++)
        {
            double v = value; int f = field, s = state;
            Run($"underlay-scale/refusal/{ParameterBits(v)}/{f}/{s}", () => {
                var item = UnderlayScaleSubject(0, 0, 0); item.ScaleZ = -7; var before = UnderlayStoredScale(item);
                UnderlayReject(item, () => {
                    if (f == 0) item.Scale = new Vector2(v, 3); else if (f == 1) item.Scale = new Vector2(4, v); else item.ScaleZ = v;
                }, s == 0 ? null : s == 1 ? Array.Empty<byte>() : UnderlayProxy);
                RawLinePointBits(before, UnderlayStoredScale(item));
            });
        }
        foreach (double value in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            double v = value;
            Run($"underlay-scale/constructor/{ParameterBits(v)}", () =>
                Throws<ArgumentOutOfRangeException>(() => new Underlay(new UnderlayPdfDefinition("scale.pdf"), Vector3.Zero, v)));
        }
        foreach (bool binary in new[] { false, true }) foreach (short code in new short[] { 41, 42, 43 })
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            short c = code; double v = value;
            Run($"underlay-scale/input-refusal/{binary}/{c}/{ParameterBits(v)}", () => {
                var doc = new DxfDocument(); doc.Entities.Add(UnderlayScaleSubject(0, 0, 0));
                using var seed = new MemoryStream(); Check(doc.Save(seed, binary), "Nonfinite seed");
                var raw = LoadRaw(seed.ToArray()); var record = raw.Sections.SelectMany(s => s.Records).Single(IsUnderlayRecord);
                int at = record.StartTagIndex + record.Tags.ToList().FindIndex(t => t.Code == c);
                // DxfTag correctly refuses nonfinite values. Inject the invalid
                // scalar into the physical transport, not through that validated API.
                using var packet = new MemoryStream();
                packet.Write(RawFixtureBytes(raw.Tags.Take(at), binary));
                if (binary)
                {
                    using var writer = new BinaryWriter(packet, Encoding.UTF8, true);
                    writer.Write(c); writer.Write(v);
                }
                else packet.Write(Encoding.UTF8.GetBytes(c.ToString(CultureInfo.InvariantCulture) + "\n"
                    + v.ToString("R", CultureInfo.InvariantCulture) + "\n"));
                byte[] tail = RawFixtureBytes(raw.Tags.Skip(at + 1), binary);
                packet.Write(tail, binary ? BinarySentinel.Length : 0, tail.Length - (binary ? BinarySentinel.Length : 0));
                using var input = new MemoryStream(packet.ToArray()); bool rejected = false;
                try { rejected = DxfDocument.Load(input) == null; }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidDataException || ex is FormatException) { rejected = true; }
                Check(rejected && input.CanRead, "Nonfinite scale accepted or caller stream closed");
            });
        }
        for (int signs = 0; signs < 8; signs++)
        {
            int s = signs;
            Run($"underlay-scale/affine-z/{s}", () => {
                var item = UnderlayScaleSubject(0, 2, s); var scale = UnderlayScaleValue(s);
                item.Scale = new Vector2(scale.X, scale.Y); item.ScaleZ = scale.Z;
                var before = UnderlayAxes(item); var matrix = new Matrix3(0, 1, 0, 1, 0, 0, 0, 0, 1);
                var expected = new[] { matrix * before[0], matrix * before[1], matrix * before[2] };
                item.TransformBy(matrix, Vector3.Zero); UnderlayCheckAxes(expected, item);
                SameDoubleBits(scale.Z, item.ScaleZ, "Affine transform changed independent stored Z");
            });
        }
        Run("underlay-scale/epsilon-independent-storage", () => {
            double saved = MathHelper.Epsilon;
            try { MathHelper.Epsilon = .001; var item = UnderlayScaleSubject(0, 0, 0);
                item.Scale = new Vector2(double.Epsilon, -double.Epsilon); item.ScaleZ = -double.Epsilon;
                RawLinePointBits(new Vector3(double.Epsilon, -double.Epsilon, -double.Epsilon), UnderlayStoredScale((Underlay)item.Clone())); }
            finally { MathHelper.Epsilon = saved; }
        });
    }
}
