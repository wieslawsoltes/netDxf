using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

internal static class SvgStreamingVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("svg/streamed paths retain round-trip numeric text across buffer boundaries", RoundTrip));
        tests.Add(("svg/streamed numeric attributes remain invariant under caller culture", Culture));
        tests.Add(("svg/nonfinite coordinates remain rejected", Nonfinite));
    }

    private static RenderFrame Frame(ImmutableArray<RenderContour> contours)
    {
        var bounds = new Bounds2(-1, -1, 1, 1);
        var item = new RenderItem("synthetic", new("synthetic"), [], new(0x80445566, .125, [], true, "0"),
            contours, false, null, null, bounds);
        var scene = new RenderScene(Guid.Empty, 0, "Model", [item], bounds, new SpatialIndex([bounds]), []);
        return new(scene, new(new(0, 0), 1, 300, 200), Background: 0x20334455);
    }

    private static string ExpectedPath(RenderFrame frame, ImmutableArray<RenderContour> contours)
    {
        // Preserve the previous wire text independently of the bounded streaming writer.
        var text = new StringBuilder();
        foreach (var contour in contours)
        {
            for (var i = 0; i < contour.Points.Length; i++)
            {
                var point = frame.Viewport.ToScreen(contour.Points[i]);
                text.Append(i == 0 ? 'M' : 'L').Append(point.X.ToString("R", CultureInfo.InvariantCulture))
                    .Append(' ').Append(point.Y.ToString("R", CultureInfo.InvariantCulture));
            }
            if (contour.Closed) text.Append('Z');
        }
        return text.ToString();
    }

    private static void RoundTrip()
    {
        double[] edgeValues = [0, -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
            1e-308, -1e-308, 1e100, -1e100, Math.PI, Math.BitIncrement(1), Math.BitDecrement(1)];
        var points = Enumerable.Range(0, 5000).Select(i => new Vec3(Math.Sin(i * .125) * 1e7, Math.Cos(i * .175) * 1e-5))
            .Concat(edgeValues.Select(value => new Vec3(value, value))).ToImmutableArray();
        ImmutableArray<RenderContour> contours = [new([], false), new([], true), new(points, false),
            new([new(1, 1), new(2, 3), new(1, 1)], true), new([], false)];
        var frame = Frame(contours);
        using var backend = new SvgRenderBackend();
        var output = backend.Render(frame);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(output.Bytes));
        XNamespace ns = "http://www.w3.org/2000/svg";
        var path = xml.Descendants(ns + "path").Single();
        Require(path.Attribute("d")!.Value == ExpectedPath(frame, contours), "Streaming changed SVG path precision or command order.");
        Require(path.Attribute("stroke")!.Value == "#445566", "Streaming changed the paint.");
        Require(path.Attribute("opacity")!.Value == (128 / 255.0).ToString("R", CultureInfo.InvariantCulture), "Streaming changed alpha precision.");
        Require(output.Bytes.AsSpan().SequenceEqual(backend.Render(frame).Bytes), "Repeated SVG rendering is nondeterministic.");
        var empty = XDocument.Parse(Encoding.UTF8.GetString(backend.Render(Frame([new([], false)])).Bytes));
        Require(!empty.Descendants(ns + "path").Any(), "An empty contour emitted a path.");
    }

    private static void Culture()
    {
        var previous = CultureInfo.CurrentCulture;
        var frame = Frame([new([new(.12345678901234567, -.9876543210987654), new(1.25, -2.75)], false)]);
        using var backend = new SvgRenderBackend();
        var expected = backend.Render(frame).Bytes;
        try
        {
            foreach (var name in new[] { "fr-FR", "ar-SA", "pl-PL" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Require(expected.AsSpan().SequenceEqual(backend.Render(frame).Bytes), "Caller culture changed SVG numeric text.");
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void Nonfinite()
    {
        using var backend = new SvgRenderBackend();
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            bool rejected = false;
            try { backend.Render(Frame([new([new(value, 0), new(1, 1)], false)])); }
            catch (ArithmeticException) { rejected = true; }
            Require(rejected, "A nonfinite SVG coordinate was accepted.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
