using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;
using SkiaSharp;

internal static class OleRasterPreviewVerification
{
    private const string Property = "$oleRasterPreview";
    private const uint Free = 0xffffffff, End = 0xfffffffe;
    private static readonly Vec3[] Corners = [new(2, 2), new(10, 2), new(10, 6), new(2, 6)];

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (bool mini in new[] { false, true })
        foreach (bool version4 in new[] { false, true })
        foreach (bool envelope in new[] { false, true })
            tests.Add(($"ole-preview/snapshot/{mini}/{version4}/{envelope}", () => Snapshot(mini, version4, envelope)));
        foreach (bool bitfields in new[] { false, true })
        foreach (bool binary in new[] { false, true })
            tests.Add(($"ole-preview/Studio Skia and SVG/{bitfields}/{binary}", () => Rendering(bitfields, binary)));
        tests.Add(("ole-preview/nested mirrored insert preserves image orientation", Nested));
        tests.Add(("ole-preview/bounded image cache survives eviction and repeated rendering", Eviction));
        tests.Add(("ole-preview/malformed storage and bitmap boundaries stay inert", Malformed));
        tests.Add(("ole-preview/unsupported native payload stays preserved", Unsupported));
    }

    private static Ole2Frame Frame(byte[] data) => new(data, new(2, 6, 0), new(10, 2, 0), "Synthetic bitmap");
    private static DxfDocument Document(Ole2Frame frame)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018); document.Entities.Add(frame); return document;
    }

    private static void Snapshot(bool mini, bool version4, bool envelope)
    {
        var bitmap = Bitmap(mini ? 2 : 64, mini ? 2 : 32, false, false);
        var compound = Compound(bitmap, version4); var bytes = envelope ? Envelope(compound) : compound;
        var frame = Frame(bytes); var source = new ShadowSession(Document(frame));
        var snapshot = source.Current; var entity = snapshot.DrawingEntities().Single();
        Require(entity[Property].Exists, "A supported stored BMP preview was not captured.");
        Require(!snapshot.Objects[entity.Id!.Value].WritableProperties.Contains(Property), "Derived raster data became editable source state.");
        var reference = entity[Property]["Reference"].Text;
        Require(Convert.FromBase64String(reference["data:image/bmp;base64,".Length..]).SequenceEqual(bitmap), "Preview extraction changed BMP bytes.");
        var scene = new CadSceneBuilder().Build(snapshot); var image = scene.Occurrences.Single().Geometry.Image;
        Require(image is not null && image.Corners.SequenceEqual(Corners), "Preview placement changed.");
        Require(!scene.Diagnostics.Any(d => d.Code is "geometry.invalid" or "geometry.unsupported"), "Supported preview was reported as unsupported.");
        Require(ReferenceEquals(snapshot, source.Refresh()), "A repeated preview capture changed the immutable snapshot.");
        Require(ReferenceEquals(entity[Property].Value, source.Refresh().DrawingEntities().Single()[Property].Value), "Repeated capture rebuilt cached raster data.");
        Require(frame.GetBinaryData().SequenceEqual(bytes), "Read-only extraction changed the OLE payload.");
        var encoded = frame.GetBinaryData(); encoded[0] ^= 255;
        Require(ReferenceEquals(snapshot, source.Refresh()), "A caller-owned binary copy changed the cached source preview.");
        var rejected = new ShadowGraphEdit(snapshot); rejected.Set(entity.Id.Value, Property, ShadowValue.From("edited preview"));
        rejected.Set(entity.Id.Value, "IsVisible", ShadowValue.From(false));
        bool refused = false;
        try { source.Apply(rejected.Build()); } catch (NotSupportedException) { refused = true; }
        Require(refused && frame.IsVisible && ReferenceEquals(snapshot, source.Current), "Derived preview edit was not rejected atomically.");
        var editor = new EditorSession(source); editor.SetProperty(entity.Id.Value, "IsVisible", ShadowValue.From(false));
        Require(!frame.IsVisible && ReferenceEquals(entity[Property].Value, source.Current.DrawingEntities().Single()[Property].Value),
            "An ordinary property edit replaced or lost the stored preview.");
        editor.Undo(); Require(frame.IsVisible && frame.GetBinaryData().SequenceEqual(bytes), "Property undo changed the inert OLE payload.");
    }

    private static void Rendering(bool bitfields, bool binary)
    {
        var payload = Envelope(Compound(Bitmap(2, 2, bitfields, bitfields), false));
        var document = Document(Frame(payload));
        using var stream = new MemoryStream(); Require(document.Save(stream, binary), "Synthetic preview save failed.");
        stream.Position = 0;
        using var studio = new Studio(false); studio.Load(stream);
        var raster = studio.Render(320, 240, 6, 4, 30, "skia", null, default);
        using var result = SKBitmap.Decode(raster.Bytes);
        Require(result is not null, "Skia preview did not render.");
        Color(result!.GetPixel(100, 90), SKColors.Red); Color(result.GetPixel(220, 90), SKColors.Lime);
        Color(result.GetPixel(100, 150), SKColors.Blue); Color(result.GetPixel(220, 150), SKColors.Yellow);
        var vector = studio.Render(320, 240, 6, 4, 30, "svg", null, default);
        var svg = XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes));
        var image = svg.Descendants().Single(e => e.Name.LocalName == "image");
        var href = image.Attribute("href")!.Value;
        Require(href.StartsWith("data:image/png;base64,", StringComparison.Ordinal), "SVG preview was not normalized to a portable raster.");
        using var embedded = SKBitmap.Decode(Convert.FromBase64String(href["data:image/png;base64,".Length..]));
        Require(embedded is not null && embedded.Width == 2 && embedded.Height == 2, "SVG preview dimensions changed.");
        Color(embedded!.GetPixel(0, 0), SKColors.Red); Color(embedded.GetPixel(1, 0), SKColors.Lime);
        Color(embedded.GetPixel(0, 1), SKColors.Blue); Color(embedded.GetPixel(1, 1), SKColors.Yellow);
        using var sourceBytes = new MemoryStream(studio.Save()); var saved = DxfDocument.Load(sourceBytes)!;
        Require(saved.Entities.All.OfType<Ole2Frame>().Single().GetBinaryData().SequenceEqual(payload), "Rendering changed the stored OLE bytes.");
    }

    private static void Nested()
    {
        var rotation = Affine3.Move(new(3, -2, 4)) * Affine3.RotateZ(0.35);
        var authored = Corners.Select(rotation.Point).ToArray();
        var payload = Envelope(Compound(Bitmap(2, 2, true, false), false), authored);
        var frame = Frame(payload); var inner = new Block("RasterDefinition"); inner.Entities.Add(frame);
        var outer = new Block("NestedRasterDefinition");
        outer.Entities.Add(new Insert(inner, new Vector3(4, 2, 0)) { Scale = new(-2, 1.5, 1) });
        var document = new DxfDocument(DxfVersion.AutoCad2018);
        document.Entities.Add(new Insert(outer, new Vector3(30, 20, 0)));
        var source = new ShadowSession(document); var scene = new CadSceneBuilder().Build(source.Current);
        var occurrence = scene.Occurrences.Single();
        Require(occurrence.InsertPath.Length == 2 && occurrence.Geometry.Image is not null, "Nested OLE preview was not expanded.");
        var corners = occurrence.Geometry.Image!.Corners;
        for (int i = 0; i < corners.Length; i++)
            Near(corners[i], new(34 - 2 * authored[i].X, 22 + 1.5 * authored[i].Y, authored[i].Z));
        Require(!new RenderCompiler().Compile(source.Current).Bounds.IsEmpty, "Nested image has no render extent.");
        Require(frame.GetBinaryData().SequenceEqual(payload), "Nested placement rewrote the immutable OLE object.");
    }

    private static void Malformed()
    {
        var cases = new List<(string Name, Action<byte[]> Corrupt)>
        {
            ("FAT cycle", bytes => Put32(bytes, 512 + 4, 1)),
            ("directory cycle", bytes => Put32(bytes, 1024 + 128 + 72, 1)),
            ("directory range", bytes => Put32(bytes, 1024 + 76, 100000)),
            ("mini FAT cycle", bytes => Put32(bytes, 1536, 0)),
            ("mini FAT range", bytes => Put32(bytes, 1536, 100000)),
            ("mini stream size", bytes => Put64(bytes, 1024 + 120, ulong.MaxValue)),
            ("stream size", bytes => Put64(bytes, 1024 + 128 + 120, ulong.MaxValue)),
            ("FAT alias", bytes => Put32(bytes, 76, 1)),
            ("DIFAT cycle", bytes => { Put32(bytes, 68, 0); Put32(bytes, 72, 1); }),
            ("sector shift", bytes => Put16(bytes, 30, 31)),
            ("missing CONTENTS", bytes => Name(bytes, 1024 + 128, "UNRELATED")),
            ("bitmap length", bytes => Put32(bytes, 2048 + 2, 5)),
            ("pixel budget", bytes => Put32(bytes, 2048 + 18, 100000)),
            ("pixel offset", bytes => Put32(bytes, 2048 + 10, 5000)),
            ("compression", bytes => Put32(bytes, 2048 + 30, 99)),
            ("overlapping masks", bytes => Put32(bytes, 2048 + 58, 0x00ff0000)),
            ("shared stream", bytes =>
            {
                Array.Copy(bytes, 1024 + 128, bytes, 1024 + 256, 128);
                Name(bytes, 1024 + 256, "ALIAS"); Put32(bytes, 1024 + 128 + 72, 2);
            })
        };
        foreach (var (name, corrupt) in cases)
        {
            var bytes = Compound(Bitmap(2, 2, true, false), false); corrupt(bytes); Reject(bytes, name);
        }
        var valid = Compound(Bitmap(2, 2, false, false), false);
        Reject(valid[..^1], "truncated sector");
        var corners = Envelope(valid); Put64(corners, 2, 0x7ff8000000000000); Reject(corners, "nonfinite corner");
        corners = Envelope(valid); Array.Clear(corners, 2, 96); Reject(corners, "collapsed corner geometry");
        corners = Envelope(valid); Put64(corners, 2 + 24 * 2, (ulong)BitConverter.DoubleToInt64Bits(100)); Reject(corners, "nonaffine corner geometry");
    }

    private static void Eviction()
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018);
        for (int i = 0; i < 10; i++)
        {
            var bitmap = Bitmap(2, 2, true, false); Put32(bitmap, 38, (uint)(i + 1));
            var corners = Corners.Select(p => p + new Vec3(i * 12, 0)).ToArray();
            document.Entities.Add(Frame(Envelope(Compound(bitmap, false), corners)));
        }
        using var stream = new MemoryStream(); Require(document.Save(stream, true), "Cache fixture save failed.");
        stream.Position = 0; using var studio = new Studio(false); studio.Load(stream);
        foreach (string backend in new[] { "skia", "svg", "skia", "svg" })
        {
            var first = studio.Render(800, 160, 60, 4, 6, backend, null, default);
            var repeated = studio.Render(800, 160, 60, 4, 6, backend, null, default);
            Require(first.Bytes.SequenceEqual(repeated.Bytes), "Image cache eviction changed repeated rendering.");
            if (backend == "skia")
            {
                using var raster = SKBitmap.Decode(repeated.Bytes);
                Color(raster.GetPixel(64, 74), SKColors.Red); Color(raster.GetPixel(712, 74), SKColors.Red);
            }
            else
                Require(XDocument.Parse(Encoding.UTF8.GetString(repeated.Bytes)).Descendants().Count(e => e.Name.LocalName == "image") == 10,
                    "Image cache eviction dropped a stored preview.");
        }
    }

    private static void Reject(byte[] bytes, string name)
    {
        var frame = Frame(bytes); var source = new ShadowSession(Document(frame));
        Require(!source.Current.DrawingEntities().Single()[Property].Exists, "Malformed preview was exposed: " + name);
        var scene = new CadSceneBuilder().Build(source.Current);
        Require(scene.Diagnostics.Any(d => d.Code == "geometry.unsupported") && !scene.Occurrences.Any(o => o.Geometry.Image is not null),
            "A malformed object acquired partial raster geometry: " + name);
        Require(frame.GetBinaryData().SequenceEqual(bytes), "Malformed preview rejection changed source bytes.");
        Require(ReferenceEquals(source.Current, source.Refresh()), "Unsupported preview capture is unstable.");
    }

    private static void Unsupported()
    {
        Reject(new byte[] { 1, 2, 3, 4 }, "unrecognized native object");
        Reject(Compound(new byte[72], false), "nonbitmap CONTENTS");
    }

    // All storage and pixel data below are independently authored. The compound
    // fixture has a single root CONTENTS stream and exercises both allocation tables.
    private static byte[] Compound(byte[] content, bool version4)
    {
        int sector = version4 ? 4096 : 512;
        bool mini = content.Length < 4096;
        int miniLength = (content.Length + 63) / 64 * 64;
        int dataLength = mini ? miniLength : content.Length;
        int dataSectors = (dataLength + sector - 1) / sector;
        int firstData = mini ? 3 : 2;
        var bytes = new byte[(firstData + dataSectors + 1) * sector];
        new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }.CopyTo(bytes, 0);
        Put16(bytes, 24, 0x3e); Put16(bytes, 26, version4 ? 4 : 3); Put16(bytes, 28, 0xfffe);
        Put16(bytes, 30, version4 ? 12 : 9); Put16(bytes, 32, 6);
        Put32(bytes, 40, version4 ? 1u : 0); Put32(bytes, 44, 1); Put32(bytes, 48, 1);
        Put32(bytes, 56, 4096); Put32(bytes, 60, mini ? 2u : End); Put32(bytes, 64, mini ? 1u : 0);
        Put32(bytes, 68, End); Put32(bytes, 72, 0);
        for (int i = 0; i < 109; i++) Put32(bytes, 76 + i * 4, i == 0 ? 0 : Free);
        for (int i = 0; i < sector / 4; i++) Put32(bytes, sector + i * 4, Free);
        Put32(bytes, sector, 0xfffffffd); Put32(bytes, sector + 4, End);
        if (mini) Put32(bytes, sector + 8, End);
        for (int i = 0; i < dataSectors; i++)
            Put32(bytes, sector + (firstData + i) * 4, i == dataSectors - 1 ? End : (uint)(firstData + i + 1));
        int directory = sector * 2;
        Entry(bytes, directory, "Root Entry", 5, mini ? (uint)firstData : End, mini ? (ulong)miniLength : 0, 1);
        Entry(bytes, directory + 128, "CONTENTS", 2, mini ? 0 : (uint)firstData, (ulong)content.Length, Free);
        if (mini)
        {
            for (int i = 0; i < sector / 4; i++) Put32(bytes, sector * 3 + i * 4, Free);
            for (int i = 0; i < miniLength / 64; i++) Put32(bytes, sector * 3 + i * 4, i == miniLength / 64 - 1 ? End : (uint)(i + 1));
        }
        content.CopyTo(bytes, (firstData + 1) * sector); return bytes;
    }

    private static void Entry(byte[] bytes, int start, string name, byte kind, uint sector, ulong length, uint child)
    {
        Name(bytes, start, name); bytes[start + 66] = kind; bytes[start + 67] = 1;
        Put32(bytes, start + 68, Free); Put32(bytes, start + 72, Free); Put32(bytes, start + 76, child);
        Put32(bytes, start + 116, sector); Put64(bytes, start + 120, length);
    }
    private static void Name(byte[] bytes, int start, string value)
    {
        Array.Clear(bytes, start, 64); Encoding.Unicode.GetBytes(value + '\0').CopyTo(bytes, start);
        Put16(bytes, start + 64, (value.Length + 1) * 2);
    }

    private static byte[] Envelope(byte[] compound, Vec3[]? corners = null)
    {
        corners ??= Corners;
        var data = new byte[160 + compound.Length]; Put16(data, 0, 0x5580);
        var ordered = new[] { corners[3], corners[2], corners[1], corners[0] };
        for (int i = 0; i < ordered.Length; i++)
        {
            Put64(data, 2 + i * 24, (ulong)BitConverter.DoubleToInt64Bits(ordered[i].X));
            Put64(data, 10 + i * 24, (ulong)BitConverter.DoubleToInt64Bits(ordered[i].Y));
            Put64(data, 18 + i * 24, (ulong)BitConverter.DoubleToInt64Bits(ordered[i].Z));
        }
        compound.CopyTo(data, 160); return data;
    }

    private static byte[] Bitmap(int width, int height, bool bitfields, bool topDown)
    {
        int bits = bitfields ? 32 : 24, offset = bitfields ? 66 : 54;
        int row = (width * bits + 31) / 32 * 4; var bytes = new byte[offset + row * height];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M'; Put32(bytes, 2, (uint)bytes.Length); Put32(bytes, 10, (uint)offset);
        Put32(bytes, 14, 40); Put32(bytes, 18, (uint)width); Put32(bytes, 22, unchecked((uint)(topDown ? -height : height)));
        Put16(bytes, 26, 1); Put16(bytes, 28, bits); Put32(bytes, 30, bitfields ? 3u : 0);
        Put32(bytes, 34, (uint)(row * height));
        if (bitfields) { Put32(bytes, 54, 0x00ff0000); Put32(bytes, 58, 0x0000ff00); Put32(bytes, 62, 0x000000ff); }
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var color = y < height / 2 ? x < width / 2 ? SKColors.Red : SKColors.Lime : x < width / 2 ? SKColors.Blue : SKColors.Yellow;
            int p = offset + (topDown ? y : height - 1 - y) * row + x * (bits / 8);
            bytes[p] = color.Blue; bytes[p + 1] = color.Green; bytes[p + 2] = color.Red;
        }
        return bytes;
    }

    private static void Put16(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)value);
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static void Put64(byte[] bytes, int offset, ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), value);
    private static void Color(SKColor actual, SKColor expected) => Require(Math.Abs(actual.Red - expected.Red) < 5 && Math.Abs(actual.Green - expected.Green) < 5
        && Math.Abs(actual.Blue - expected.Blue) < 5 && actual.Alpha > 250, "Stored bitmap color or orientation changed.");
    private static void Near(Vec3 actual, Vec3 expected) => Require(Vec3.Distance(actual, expected) < 1e-8, "Preview corner transform changed.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
