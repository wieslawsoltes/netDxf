using System.Globalization;
using netDxf;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string ViewApplication = "DXF_VIEW_CONFORMANCE";

    private static void RegisterNamedViewTests()
    {
        Run("view/api/defaults", () =>
        {
            var view = new View("Default");
            Equal(Vector3.UnitZ, view.ViewDirection, "default view direction");
            Equal(Vector2.Zero, view.ViewCenter, "default view center");
            Near(40, view.LensLength, "legacy default lens length");
            Near(1, view.Height, "default view height");
            Near(1, view.Width, "default view width");
            Equal(ViewFlags.None, view.Flags, "default flags");
            Equal(ViewRenderMode.TwoDimensionalOptimized, view.RenderMode, "default render mode");
            Check(!view.IsCameraPlottable && !view.IsPaperSpace, "Unexpected view defaults.");
            Throws<ArgumentNullException>(() => new View(null!));
        });
        Run("view/api/aliases", () =>
        {
            var view = new View("Aliases") { Camera = new Vector3(2, 3, 4), Fov = 65, Viewmode = ViewModeFlags.Perspective };
            Equal(new Vector3(2, 3, 4), view.ViewDirection, "camera alias must not normalize");
            Near(65, view.LensLength, "lens alias");
            Equal(ViewModeFlags.Perspective, view.ViewMode, "mode alias");
            view.ViewDirection = new Vector3(5, 6, 7); view.LensLength = 82; view.ViewMode = ViewModeFlags.BackClippingPlane;
            Equal(new Vector3(5, 6, 7), view.Camera, "reverse camera alias");
            Near(82, view.Fov, "reverse lens alias");
            Equal(ViewModeFlags.BackClippingPlane, view.Viewmode, "reverse mode alias");
        });
        Run("view/api/validation", () =>
        {
            var view = new View("Validation");
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Throws<ArgumentOutOfRangeException>(() => view.Height = bad);
                Throws<ArgumentOutOfRangeException>(() => view.Width = bad);
                Throws<ArgumentOutOfRangeException>(() => view.LensLength = bad);
                Throws<ArgumentOutOfRangeException>(() => view.Rotation = bad);
                Throws<ArgumentOutOfRangeException>(() => view.FrontClippingPlane = bad);
                Throws<ArgumentOutOfRangeException>(() => view.BackClippingPlane = bad);
                Throws<ArgumentOutOfRangeException>(() => view.Target = new Vector3(1, bad, 3));
                Throws<ArgumentOutOfRangeException>(() => view.ViewCenter = new Vector2(bad, 2));
                Throws<ArgumentOutOfRangeException>(() => view.ViewDirection = new Vector3(1, 2, bad));
            }
            foreach (double bad in new[] { 0.0, -1.0 })
            {
                Throws<ArgumentOutOfRangeException>(() => view.Height = bad);
                Throws<ArgumentOutOfRangeException>(() => view.Width = bad);
                Throws<ArgumentOutOfRangeException>(() => view.LensLength = bad);
            }
            Throws<ArgumentException>(() => view.Camera = Vector3.Zero);
            Throws<ArgumentOutOfRangeException>(() => view.RenderMode = (ViewRenderMode)7);
            Throws<ArgumentOutOfRangeException>(() => view.RenderMode = (ViewRenderMode)(-1));
            Throws<ArgumentOutOfRangeException>(() => view.ViewMode = (ViewModeFlags)65536);
            Equal(Vector3.UnitZ, view.ViewDirection, "rejected direction assignment changed state");
            Near(1, view.Height, "rejected height assignment changed state");
        });
        Run("view/api/flags", () =>
        {
            var view = new View("Flags") { Flags = ViewFlags.ExternallyDependent | ViewFlags.ExternalReferenceResolved | ViewFlags.Referenced };
            ViewFlags original = view.Flags;
            view.IsPaperSpace = true;
            Equal(original | ViewFlags.PaperSpace, view.Flags, "set paper flag");
            view.IsPaperSpace = false;
            Equal(original, view.Flags, "clear paper flag must retain other flags");
        });
        Run("view/api/clone", () =>
        {
            View view = NamedView(3, DxfVersion.AutoCad2018);
            var copy = (View)view.Clone("Copy");
            AssertViewValues(view, copy);
            byte[] bytes = (byte[])copy.XData[ViewApplication].XDataRecord[1].Value;
            bytes[0] = 255;
            Equal((byte)3, ((byte[])view.XData[ViewApplication].XDataRecord[1].Value)[0], "view clone binary metadata isolation");
            Check(copy.Owner == null && copy.Handle == null, "Clone retained document ownership.");
        });
        Run("view/api/collection", () =>
        {
            var document = new DxfDocument();
            var view = new View("Named");
            Check(document.Views.Add(view) == view, "Adding a named view failed.");
            Check(document.Views.Add(new View("NAMED")) == view, "View names must be case insensitive.");
            string handle = view.Handle;
            Check(document.GetObjectByHandle(handle) == view, "View was not registered by handle.");
            view.Name = "Renamed";
            Check(document.Views.Contains("renamed") && !document.Views.Contains("Named"), "View rename registry failed.");
            Check(document.Views.Remove(view), "Unreferenced view removal failed.");
            Check(document.GetObjectByHandle(handle) == null && view.Owner == null && view.Handle == null, "View removal leaked ownership.");
        });
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                string prefix = $"view/{v}/{(b ? "binary" : "text")}";
                Run(prefix + "/document", () => NamedViewRoundTrip(v, b));
                Run(prefix + "/authored", () => NamedViewAuthored(v, b));
                Run(prefix + "/minimal-defaults", () => NamedViewDefaults(v, b));
                Run(prefix + "/malformed", () => NamedViewMalformed(v, b));
                Run(prefix + "/camera-version-gate", () => NamedViewCameraGate(v, b));
            }
        }
    }

    private static View NamedView(int index, DxfVersion version)
    {
        var view = new View("View_" + index + "_Zażółć_東京")
        {
            ViewCenter = new Vector2(index + .25, -index - .5),
            ViewDirection = new Vector3(index + 1, 2, 3),
            Target = new Vector3(index + 10, -20, 30),
            Height = index + 12.125, Width = index + 20.25,
            LensLength = index + 85.5, Rotation = index - 37.25,
            FrontClippingPlane = index - 4.5, BackClippingPlane = index + 50.5,
            Flags = ViewFlags.Referenced | (index % 2 == 0 ? ViewFlags.PaperSpace : ViewFlags.None),
            ViewMode = ViewModeFlags.Perspective | ViewModeFlags.FrontClippingPlane | ViewModeFlags.BackClippingPlane | ViewModeFlags.FrontClipNotAtEye,
            RenderMode = (ViewRenderMode)index,
            IsCameraPlottable = version >= DxfVersion.AutoCad2007 && index % 2 == 1
        };
        var metadata = new XData(new ApplicationRegistry(ViewApplication));
        metadata.XDataRecord.Add(new XDataRecord(XDataCode.String, "View metadata " + index));
        metadata.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { (byte)index, 0, 255 }));
        view.XData.Add(metadata);
        return view;
    }

    private static void AssertViewValues(View expected, View actual)
    {
        Equal(expected.ViewCenter, actual.ViewCenter, "view center");
        Equal(expected.ViewDirection, actual.ViewDirection, "view direction");
        Equal(expected.Target, actual.Target, "view target");
        Near(expected.Height, actual.Height, "view height"); Near(expected.Width, actual.Width, "view width");
        Near(expected.LensLength, actual.LensLength, "lens length"); Near(expected.Rotation, actual.Rotation, "twist angle");
        Near(expected.FrontClippingPlane, actual.FrontClippingPlane, "front plane"); Near(expected.BackClippingPlane, actual.BackClippingPlane, "back plane");
        Equal(expected.Flags, actual.Flags, "view flags"); Equal(expected.ViewMode, actual.ViewMode, "view mode");
        Equal(expected.RenderMode, actual.RenderMode, "render mode"); Equal(expected.IsCameraPlottable, actual.IsCameraPlottable, "plottable camera");
    }

    private static void NamedViewRoundTrip(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        for (int index = 0; index < 7; index++) document.Views.Add(NamedView(index, version));
        document.Views.XData.Add(BinaryXData(new byte[] { 11, 22, 33 }));
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Named-view save failed.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Named-view load failed.");
        Equal(7, loaded.Views.Count, "named-view count");
        Check(new byte[] { 11, 22, 33 }.SequenceEqual(BinaryValue(loaded.Views.XData[CloneApplication])), "VIEW table metadata was lost.");
        foreach (View expected in document.Views)
        {
            View actual = loaded.Views[expected.Name];
            AssertViewValues(expected, actual);
            Equal(expected.Handle, actual.Handle, "view handle");
            Check(actual.Owner == loaded.Views && loaded.GetObjectByHandle(actual.Handle) == actual, "View owner/handle graph failed.");
            Equal((string)expected.XData[ViewApplication].XDataRecord[0].Value, (string)actual.XData[ViewApplication].XDataRecord[0].Value, "view string metadata");
            Check(((byte[])expected.XData[ViewApplication].XDataRecord[1].Value).SequenceEqual((byte[])actual.XData[ViewApplication].XDataRecord[1].Value), "View binary metadata was lost.");
        }
        output.Position = 0;
        object reader = NewCodeReader(output, binary);
        var records = new List<List<(short Code, object Value)>>();
        List<(short Code, object Value)>? record = null;
        Invoke(reader, "Next");
        while (!(TagCode(reader) == 0 && (string)Invoke(reader, "ReadString")! == "EOF"))
        {
            short code = TagCode(reader);
            object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0)
            {
                record = (string)value == "VIEW" ? new List<(short, object)>() : null;
                if (record != null) records.Add(record);
            }
            record?.Add((code, value));
            Invoke(reader, "Next");
        }
        Equal(7, records.Count, "writer VIEW record count");
        for (int i = 0; i < records.Count; i++)
        {
            var tags = records[i];
            Equal("AcDbViewTableRecord", (string)tags.Last(tag => tag.Code == 100).Value, "VIEW subclass marker");
            Equal(document.Views.Handle, (string)tags.Single(tag => tag.Code == 330).Value, "VIEW owner tag");
            Near(i + 85.5, (double)tags.Single(tag => tag.Code == 42).Value, "writer lens group 42");
            Near(i + .25, (double)tags.Single(tag => tag.Code == 10).Value, "writer DCS center group 10");
            Near(i + 1, (double)tags.Single(tag => tag.Code == 11).Value, "writer direction group 11");
            Near(i + 10, (double)tags.Single(tag => tag.Code == 12).Value, "writer target group 12");
            Equal((short)i, (short)tags.Single(tag => tag.Code == 281).Value, "writer rendering group 281");
            Equal(version >= DxfVersion.AutoCad2007, tags.Any(tag => tag.Code == 73), "camera tag version boundary");
        }
    }

    private static void NamedViewAuthored(DxfVersion version, bool binary)
    {
        var tags = new List<(short Code, object Value)>
        {
            (42, 72.5), (32, 33.0), (20, -2.5), (2, "Authored"), (10, 1.25),
            (11, 2.0), (31, 4.0), (21, 3.0), (12, 11.0), (22, 22.0),
            (41, 120.0), (40, 80.0), (50, -15.0), (43, -5.0), (44, 55.0),
            (70, (short)113), (71, (short)31), (281, (short)6), (72, (short)0)
        };
        if (version >= DxfVersion.AutoCad2007) tags.Add((73, (short)1));
        using var fixture = AuthoredViewFixture(version, binary, tags);
        var document = DxfDocument.Load(fixture) ?? throw new InvalidOperationException("Authored VIEW failed to load.");
        var expected = new View("Authored")
        {
            ViewCenter = new Vector2(1.25, -2.5), ViewDirection = new Vector3(2, 3, 4), Target = new Vector3(11, 22, 33),
            Width = 120, Height = 80, LensLength = 72.5, Rotation = -15, FrontClippingPlane = -5, BackClippingPlane = 55,
            Flags = (ViewFlags)113, ViewMode = (ViewModeFlags)31, RenderMode = ViewRenderMode.GouraudShadedWithWireframe,
            IsCameraPlottable = version >= DxfVersion.AutoCad2007
        };
        Equal(1, document.Views.Count, "authored VIEW count");
        AssertViewValues(expected, document.Views["Authored"]);
        Equal("B", document.Views["Authored"].Handle, "authored handle");
    }

    private static void NamedViewDefaults(DxfVersion version, bool binary)
    {
        using var fixture = AuthoredViewFixture(version, binary, new[] { ((short)2, (object)"Minimal") });
        var document = DxfDocument.Load(fixture) ?? throw new InvalidOperationException("Minimal VIEW failed to load.");
        AssertViewValues(new View("Minimal"), document.Views["Minimal"]);
    }

    private static void NamedViewMalformed(DxfVersion version, bool binary)
    {
        ExpectViewLoadFailure<FormatException>(version, binary, Array.Empty<(short, object)>());
        ExpectViewLoadFailure<ArgumentOutOfRangeException>(version, binary, new[] { ((short)2, (object)"Bad"), ((short)40, (object)0.0) });
        ExpectViewLoadFailure<ArgumentException>(version, binary, new[] { ((short)2, (object)"Bad"), ((short)11, (object)0.0), ((short)21, (object)0.0), ((short)31, (object)0.0) });
        ExpectViewLoadFailure<ArgumentOutOfRangeException>(version, binary, new[] { ((short)2, (object)"Bad"), ((short)281, (object)(short)7) });
        ExpectViewLoadFailure<FormatException>(version, binary, new[] { ((short)2, (object)"Bad"), ((short)73, (object)(short)2) });
    }

    private static void ExpectViewLoadFailure<T>(DxfVersion version, bool binary, IEnumerable<(short Code, object Value)> tags) where T : Exception
    {
        using var fixture = AuthoredViewFixture(version, binary, tags);
#if DEBUG
        Throws<T>(() => DxfDocument.Load(fixture));
#else
        Check(DxfDocument.Load(fixture) == null, "Malformed named-view data was accepted.");
#endif
    }

    private static void NamedViewCameraGate(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        document.Views.Add(new View("Camera") { IsCameraPlottable = true });
        using var stream = new MemoryStream();
        if (version < DxfVersion.AutoCad2007)
        {
#if DEBUG
            Throws<NotSupportedException>(() => document.Save(stream, binary));
#else
            Check(!document.Save(stream, binary), "Down-saving silently discarded a plottable camera.");
#endif
        }
        else
        {
            Check(document.Save(stream, binary), "Supported camera failed to save.");
            stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Camera failed to load.");
            Check(loaded.Views["Camera"].IsCameraPlottable, "Plottable camera was lost.");
        }
    }

    private static MemoryStream AuthoredViewFixture(DxfVersion version, bool binary, IEnumerable<(short Code, object Value)> tags)
    {
        var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, binary);
        void Tag(short code, object value) => Invoke(writer, "Write", code, value);
        string acadver = version switch
        {
            DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018",
            DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024",
            DxfVersion.AutoCad2013 => "AC1027", DxfVersion.AutoCad2018 => "AC1032",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        Tag(0, "SECTION"); Tag(2, "HEADER"); Tag(9, "$ACADVER"); Tag(1, acadver);
        Tag(9, "$DWGCODEPAGE"); Tag(3, "ANSI_1252"); Tag(9, "$HANDSEED"); Tag(5, "FFFF"); Tag(0, "ENDSEC");
        Tag(0, "SECTION"); Tag(2, "TABLES"); Tag(0, "TABLE"); Tag(2, "VIEW");
        Tag(5, "A"); Tag(330, "0"); Tag(100, "AcDbSymbolTable"); Tag(70, (short)1);
        Tag(0, "VIEW"); Tag(5, "B"); Tag(330, "A"); Tag(100, "AcDbSymbolTableRecord"); Tag(100, "AcDbViewTableRecord");
        foreach (var tag in tags) Tag(tag.Code, tag.Value);
        Tag(0, "ENDTAB"); Tag(0, "ENDSEC");
        Tag(0, "SECTION"); Tag(2, "OBJECTS"); Tag(0, "DICTIONARY"); Tag(5, "C"); Tag(330, "0");
        Tag(100, "AcDbDictionary"); Tag(280, (short)0); Tag(281, (short)1); Tag(0, "ENDSEC"); Tag(0, "EOF");
        Invoke(writer, "Flush"); stream.Position = 0;
        return stream;
    }
}
