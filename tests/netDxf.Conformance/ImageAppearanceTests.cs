// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly byte[] ImageAppearanceBytes = Enumerable.Range(0, 300).Select(i => (byte)(i * 29)).ToArray();
    private static byte[]? AppearanceCache(int state) => state == 0 ? null : state == 1 ? Array.Empty<byte>() : ImageAppearanceBytes;
    private static Image AppearanceImage() => new(
        new ImageDefinition("AP_IMAGE", "appearance.png", 8, 96, 6, 96, ImageResolutionUnits.Inches),
        new Vector3(3, -5, 7), 4, 3)
    {
        Clipping = true, Brightness = 61, Contrast = 37, Fade = 9,
        DisplayOptions = (ImageDisplayFlags)7,
        ClippingBoundary = new ClippingBoundary(1, 1, 6, 4)
    };
    private static void AppearanceEdit(Image item, int edit)
    {
        switch (edit)
        {
            case 0: break;
            case 1: item.Brightness = 0; break;
            case 2: item.Contrast = 100; break;
            case 3: item.Fade = 90; break;
            case 4: item.DisplayOptions = (ImageDisplayFlags)0; break;
            case 5: item.Clipping = false; break;
            case 6: item.ClippingBoundary = new ClippingBoundary(new[] {
                new Vector2(.5, .5), new Vector2(6, 1), new Vector2(2, 5) }); break;
            case 7: item.ClippingBoundary = null; break;
        }
    }
    private static void AppearanceNoOp(Image item, int property)
    {
        switch (property)
        {
            case 0: item.Brightness = item.Brightness; break;
            case 1: item.Contrast = item.Contrast; break;
            case 2: item.Fade = item.Fade; break;
            case 3: item.DisplayOptions = item.DisplayOptions; break;
            case 4: item.Clipping = item.Clipping; break;
            case 5: item.ClippingBoundary = item.ClippingBoundary; break;
        }
    }
    private static void CheckAppearanceCache(Image item, byte[]? expected)
    {
        Check(ImageProxyEqual(expected, item.ProxyGraphics), "Appearance proxy state/payload changed");
    }
    private static void CheckAppearance(Image item, int edit, byte[]? expectedCache)
    {
        Equal((short)(edit == 1 ? 0 : 61), item.Brightness, "IMAGE brightness");
        Equal((short)(edit == 2 ? 100 : 37), item.Contrast, "IMAGE contrast");
        Equal((short)(edit == 3 ? 90 : 9), item.Fade, "IMAGE fade");
        Equal((ImageDisplayFlags)(edit == 4 ? 0 : 7), item.DisplayOptions, "IMAGE display flags");
        Equal(edit != 5, item.Clipping, "IMAGE clipping enabled");
        var vertices = edit == 6 ? new[] { new Vector2(.5, .5), new Vector2(6, 1), new Vector2(2, 5) }
            : edit == 7 ? new[] { Vector2.Zero, new Vector2(8, 6) } : new[] { new Vector2(1, 1), new Vector2(7, 5) };
        Equal(edit == 6 ? ClippingBoundaryType.Polygonal : ClippingBoundaryType.Rectangular,
            item.ClippingBoundary.Type, "IMAGE clipping type");
        Equal(vertices.Length, item.ClippingBoundary.Vertexes.Count, "IMAGE clipping count");
        for (int i = 0; i < vertices.Length; i++)
        {
            SameDoubleBits(vertices[i].X, item.ClippingBoundary.Vertexes[i].X, "Pixel clip X");
            SameDoubleBits(vertices[i].Y, item.ClippingBoundary.Vertexes[i].Y, "Pixel clip Y");
        }
        SameDoubleBits(4, item.Width, "Appearance changed width");
        SameDoubleBits(3, item.Height, "Appearance changed height");
        RawLinePointBits(new Vector3(3, -5, 7), item.Position);
        Equal("AP_IMAGE", item.Definition.Name, "Appearance changed definition");
        Equal(8, item.Definition.Width, "Definition pixel width");
        Equal(6, item.Definition.Height, "Definition pixel height");
        CheckAppearanceCache(item, expectedCache);
    }
    private static void RegisterImageAppearanceTests()
    {
        for (int property = 0; property < 6; property++) for (int state = 0; state < 3; state++)
        {
            int p = property, s = state;
            Run($"image-appearance/no-op/{p}/{s}", () =>
            {
                var item = AppearanceImage(); var boundary = item.ClippingBoundary; var definition = item.Definition;
                item.ProxyGraphics = AppearanceCache(s);
                var geometry = ImageGeometryBits(item);
                AppearanceNoOp(item, p);
                CheckAppearance(item, 0, AppearanceCache(s));
                Check(ReferenceEquals(boundary, item.ClippingBoundary) && ReferenceEquals(definition, item.Definition), "No-op dependencies changed");
                Check(geometry.SequenceEqual(ImageGeometryBits(item)), "No-op geometry changed");
            });
        }
        for (int edit = 1; edit < 8; edit++) for (int state = 0; state < 3; state++)
        {
            int e = edit, s = state;
            Run($"image-appearance/change/{e}/{s}", () =>
            {
                var item = AppearanceImage(); var boundary = item.ClippingBoundary; var definition = item.Definition;
                var geometry = ImageGeometryBits(item); item.ProxyGraphics = AppearanceCache(s);
                var clone = (Image)item.Clone();
                AppearanceEdit(item, e);
                CheckAppearance(item, e, null); CheckAppearance(clone, 0, AppearanceCache(s));
                Check(ReferenceEquals(definition, item.Definition), "Edit changed definition");
                Check(geometry.SequenceEqual(ImageGeometryBits(item)), "Edit changed geometric state");
                if (e < 6) Check(ReferenceEquals(boundary, item.ClippingBoundary), "Scalar edit changed boundary");
                else Check(!ReferenceEquals(boundary, item.ClippingBoundary), "Boundary replacement was not committed");
                // Reattaching an independent cache then assigning the actual value/reference is a no-op.
                item.ProxyGraphics = AppearanceCache(s);
                AppearanceNoOp(item, e < 6 ? e - 1 : 5);
                CheckAppearance(item, e, AppearanceCache(s));
            });
        }
        for (int property = 0; property < 3; property++)
        foreach (short value in new short[] { short.MinValue, -1, 101, short.MaxValue })
        for (int state = 0; state < 3; state++)
        {
            int p = property, s = state; short v = value;
            Run($"image-appearance/refusal/{p}/{v}/{s}", () =>
            {
                var item = AppearanceImage(); item.ProxyGraphics = AppearanceCache(s);
                var boundary = item.ClippingBoundary;
                Throws<ArgumentOutOfRangeException>(() => SetAppearanceScalar(item, p, v));
                CheckAppearance(item, 0, AppearanceCache(s));
                Check(ReferenceEquals(boundary, item.ClippingBoundary), "Refusal changed boundary");
            });
        }
        for (int property = 0; property < 3; property++) foreach (short value in new short[] { 0, 1, 50, 99, 100 })
        {
            int p = property; short v = value;
            Run($"image-appearance/range/{p}/{v}", () =>
            {
                var item = AppearanceImage(); item.ProxyGraphics = ImageAppearanceBytes;
                SetAppearanceScalar(item, p, v); CheckAppearanceCache(item, null);
                Equal(v, p == 0 ? item.Brightness : p == 1 ? item.Contrast : item.Fade, "Accepted endpoint");
                item.ProxyGraphics = ImageAppearanceBytes;
                SetAppearanceScalar(item, p, v); CheckAppearanceCache(item, ImageAppearanceBytes);
            });
        }
        Run("image-appearance/default-reset-is-replacement", () =>
        {
            var item = AppearanceImage(); item.ClippingBoundary = null;
            var boundary = item.ClippingBoundary; item.ProxyGraphics = ImageAppearanceBytes;
            item.ClippingBoundary = null;
            CheckAppearance(item, 7, null);
            Check(!ReferenceEquals(boundary, item.ClippingBoundary), "Null reset must keep existing new-boundary semantics");
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        for (int placement = 0; placement < 4; placement++)
        {
            int p = placement;
            Run($"image-appearance/wire/{version}/{binary}/{p}", () =>
            {
                var doc = new DxfDocument(version); doc.Comments.Clear();
                var images = new List<Image>();
                for (int edit = 0; edit < 8; edit++) for (int state = 0; state < 3; state++)
                {
                    var item = AppearanceImage(); item.Layer = new Layer($"IMAGE_AP_{edit:D2}_{state}");
                    var keep = new XData(new ApplicationRegistry("IMAGE_AP_KEEP"));
                    keep.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); item.XData.Add(keep);
                    item.ProxyGraphics = AppearanceCache(state);
                    AppearanceEdit(item, edit);
                    CheckAppearance(item, edit, edit == 0 ? AppearanceCache(state) : null);
                    images.Add(item);
                }
                if (p == 0) doc.Entities.Add(images);
                else if (p == 1)
                {
                    doc.Layouts.Add(new Layout("AP_PAPER"));
                    foreach (var item in images) doc.Layouts["AP_PAPER"].AssociatedBlock.Entities.Add(item);
                }
                else
                {
                    var holder = new Block("AP_HOLDER", images);
                    if (p == 2) doc.Entities.Add(new Insert(holder)); else doc.Blocks.Add(holder);
                }
                doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)));
                string[] handles = images.Select(i => i.Handle).ToArray();
                CheckAppearanceDocument(doc, handles);
                string stem = $"image-appearance-{version}-{binary}-{p}";
                using var input = new MemoryStream();
                Check(doc.Save(input, binary), "Appearance source save");
                CheckAppearanceDocument(doc, handles);
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), input.ToArray());
                input.Position = 0;
                var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Appearance source load");
                CheckAppearanceDocument(loaded, handles);
                foreach (bool output in new[] { false, true })
                {
                    using var stream = new MemoryStream(); Check(loaded.Save(stream, output), "Appearance output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{output}.dxf"), stream.ToArray());
                    stream.Position = 0;
                    CheckAppearanceDocument(DxfDocument.Load(stream) ?? throw new InvalidOperationException("Appearance reload"), handles);
                }
                // Loaded public edits must invalidate, although hydration retained the unchanged source cache.
                var retained = loaded.Blocks.SelectMany(b => b.Entities).OfType<Image>().Single(i => i.Layer.Name == "IMAGE_AP_00_2");
                retained.Brightness = 0; CheckAppearance(retained, 1, null);
                Check(input.CanRead, "Reader closed caller stream");
            });
        }
    }
    private static void SetAppearanceScalar(Image item, int property, short value)
    {
        if (property == 0) item.Brightness = value;
        else if (property == 1) item.Contrast = value;
        else item.Fade = value;
    }
    private static void CheckAppearanceDocument(DxfDocument doc, string[] handles)
    {
        var images = doc.Blocks.SelectMany(b => b.Entities).OfType<Image>().OrderBy(i => i.Layer.Name, StringComparer.Ordinal).ToArray();
        Equal(24, images.Length, "Appearance inventory");
        for (int i = 0; i < images.Length; i++)
        {
            int edit = i / 3, state = i % 3; var item = images[i];
            Equal(handles[i], item.Handle, "Appearance handle changed");
            CheckAppearance(item, edit, edit == 0 ? AppearanceCache(state) : null);
            Equal("unchanged", (string)item.XData["IMAGE_AP_KEEP"].XDataRecord.Single().Value, "Appearance changed XData");
            Check(ReferenceEquals(doc.ImageDefinitions["AP_IMAGE"], item.Definition), "Definition canonical identity changed");
            var clone = (Image)item.Clone();
            CheckAppearance(clone, edit, edit == 0 ? AppearanceCache(state) : null);
            Check(!ReferenceEquals(item.ClippingBoundary, clone.ClippingBoundary), "Clone boundary alias");
        }
        Equal(0, doc.Objects.Validate().Count, "Appearance graph validation");
        var line = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), line.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), line.EndPoint);
    }
}
