using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Only explicitly supplied raster bytes are embedded; DXF paths never trigger I/O.</summary>
public sealed record SvgImageData(string ContentType, ReadOnlyMemory<byte> Data);
public interface ISvgImageResolver { SvgImageData? Resolve(string reference); }

/// <summary>Runtime-swappable vector backend sharing geometry, fill and dash semantics with Skia.</summary>
public sealed class SvgRenderBackend : IRenderBackend
{
    private readonly ISvgImageResolver? images;
    public SvgRenderBackend(ISvgImageResolver? images = null) => this.images = images;
    public string Id => "svg";
    public void Dispose() { }

    public RenderImage Render(RenderFrame frame, CancellationToken cancellationToken = default)
    {
        frame.Viewport.Validate();
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, OmitXmlDeclaration = true }))
        {
            var nextId = 0;
            xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            Attribute("width", frame.Viewport.Width); Attribute("height", frame.Viewport.Height);
            xml.WriteAttributeString("viewBox", $"0 0 {frame.Viewport.Width} {frame.Viewport.Height}");
            xml.WriteAttributeString("overflow", "hidden");
            xml.WriteStartElement("rect"); Attribute("width", frame.Viewport.Width); Attribute("height", frame.Viewport.Height);
            xml.WriteAttributeString("fill", Color(frame.Background)); xml.WriteEndElement();
            foreach (var item in frame.VisibleItems())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var selected = frame.Selection?.Contains(item.Key) == true;
                var color = selected ? frame.SelectionColor : item.Style.Argb;
                foreach (var region in item.Regions)
                {
                    var fill = Color(color);
                    if (!selected && region.Gradient is { } gradient) fill = "url(#" + WriteGradient(gradient) + ")";
                    WritePath(region.Contours, fill, null, region.FillRule, color, 0);
                }
                if (item.Filled) WritePath(item.Contours, Color(color), null, CadFillRule.EvenOdd, color, 0);
                var stroke = Math.Max(1, item.Style.Lineweight * 96 / 25.4);
                var contours = StrokePattern.GetContours(item);
                WritePath(contours, "none", Color(color), CadFillRule.EvenOdd, color, stroke);
                foreach (var contour in contours)
                {
                    if (contour.Points.Length != 2 || contour.Points[0] != contour.Points[1]) continue;
                    var p = frame.Viewport.ToScreen(contour.Points[0]);
                    xml.WriteStartElement("circle"); Attribute("cx", p.X); Attribute("cy", p.Y); Attribute("r", Math.Max(0.5, stroke / 2));
                    xml.WriteAttributeString("fill", Color(color)); Attribute("opacity", (color >> 24) / 255.0); xml.WriteEndElement();
                }
                if (item.Image is { Visible: true } image) WriteImage(image, selected, color, stroke);
                if (item.Text is { } text)
                {
                    var origin = frame.Viewport.ToScreen(text.Origin); var scale = frame.Viewport.PixelsPerUnit;
                    xml.WriteStartElement("g");
                    xml.WriteAttributeString("transform", $"matrix({N(text.XAxis.X * scale)} {N(-text.XAxis.Y * scale)} {N(-text.YAxis.X * scale)} {N(text.YAxis.Y * scale)} {N(origin.X)} {N(origin.Y)})");
                    xml.WriteAttributeString("fill", Color(color)); Attribute("opacity", (color >> 24) / 255.0);
                    foreach (var line in TextLayout.Layout(text, s => s.Length * text.Height * 0.6))
                    {
                        xml.WriteStartElement("text"); Attribute("x", line.X); Attribute("y", line.Y); Attribute("font-size", text.Height);
                        xml.WriteAttributeString("font-family", text.FontFamily);
                        xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                        xml.WriteString(line.Text); xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                }
            }
            xml.WriteEndElement();

            void Attribute(string name, double value) => xml.WriteAttributeString(name, N(value));
            string PathData(IEnumerable<RenderContour> contours)
            {
                var path = new StringBuilder();
                foreach (var contour in contours)
                {
                    foreach (var (point, index) in contour.Points.Select((point, index) => (point, index)))
                    {
                        var p = frame.Viewport.ToScreen(point);
                        path.Append(index == 0 ? 'M' : 'L').Append(N(p.X)).Append(' ').Append(N(p.Y));
                    }
                    if (contour.Closed) path.Append('Z');
                }
                return path.ToString();
            }
            void WritePath(IEnumerable<RenderContour> contours, string fill, string? stroke, CadFillRule rule, uint color, double width)
            {
                var data = PathData(contours);
                if (data.Length == 0) return;
                xml.WriteStartElement("path"); xml.WriteAttributeString("d", data);
                xml.WriteAttributeString("fill", fill); xml.WriteAttributeString("fill-rule", rule == CadFillRule.NonZero ? "nonzero" : "evenodd");
                if (stroke is not null)
                {
                    xml.WriteAttributeString("stroke", stroke); Attribute("stroke-width", width);
                    xml.WriteAttributeString("stroke-linecap", "round"); xml.WriteAttributeString("stroke-linejoin", "round");
                }
                Attribute("opacity", (color >> 24) / 255.0); xml.WriteEndElement();
            }
            string WriteGradient(CadGradient gradient)
            {
                var id = "shadow-gradient-" + nextId++;
                var plan = GradientPlan.Create(gradient);
                xml.WriteStartElement("defs"); xml.WriteStartElement(plan.Radial ? "radialGradient" : "linearGradient");
                xml.WriteAttributeString("id", id); xml.WriteAttributeString("gradientUnits", "userSpaceOnUse");
                xml.WriteAttributeString("color-interpolation", "sRGB");
                var origin = frame.Viewport.ToScreen(gradient.Frame.Translation); var scale = frame.Viewport.PixelsPerUnit;
                xml.WriteAttributeString("gradientTransform", $"matrix({N(gradient.Frame.X.X * scale)} {N(-gradient.Frame.X.Y * scale)} {N(gradient.Frame.Y.X * scale)} {N(-gradient.Frame.Y.Y * scale)} {N(origin.X)} {N(origin.Y)})");
                if (plan.Radial) { Attribute("cx", plan.Start.X); Attribute("cy", plan.Start.Y); Attribute("r", plan.Radius); }
                else { Attribute("x1", plan.Start.X); Attribute("y1", plan.Start.Y); Attribute("x2", plan.End.X); Attribute("y2", plan.End.Y); }
                for (var i = 0; i < plan.Colors.Length; i++)
                {
                    xml.WriteStartElement("stop"); Attribute("offset", plan.Positions[i]);
                    xml.WriteAttributeString("stop-color", Color(plan.Colors[i])); Attribute("stop-opacity", (plan.Colors[i] >> 24) / 255.0); xml.WriteEndElement();
                }
                xml.WriteEndElement(); xml.WriteEndElement(); return id;
            }
            void WriteImage(CadImage image, bool selected, uint color, double width)
            {
                if (image.Corners.Length != 4) return;
                var data = selected ? null : images?.Resolve(image.FileName);
                if (data is null)
                {
                    var points = !image.ClipOutside && !image.ClipPolygon.IsEmpty ? image.ClipPolygon : image.Corners;
                    WritePath([new RenderContour(points, true)], "none", Color(color), CadFillRule.EvenOdd, color, width); return;
                }
                if (data.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
                    throw new ArgumentException("The SVG image resolver may supply PNG, JPEG or WebP raster bytes only.");
                if (data.Data.IsEmpty || data.Data.Length > 16 * 1024 * 1024) throw new ArgumentException("Embedded image byte budget exceeded.");
                string? clipId = null;
                if (!image.ClipPolygon.IsDefaultOrEmpty)
                {
                    clipId = "shadow-image-clip-" + nextId++;
                    xml.WriteStartElement("defs"); xml.WriteStartElement("clipPath"); xml.WriteAttributeString("id", clipId);
                    xml.WriteAttributeString("clipPathUnits", "userSpaceOnUse");
                    xml.WriteStartElement("path");
                    var contours = image.ClipOutside
                        ? new[] { new RenderContour(image.Corners, true), new RenderContour(image.ClipPolygon, true) }
                        : new[] { new RenderContour(image.ClipPolygon, true) };
                    xml.WriteAttributeString("d", PathData(contours)); xml.WriteAttributeString("clip-rule", "evenodd");
                    xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
                }
                var filter = WriteImageFilter(image);
                xml.WriteStartElement("g");
                if (clipId is not null) xml.WriteAttributeString("clip-path", "url(#" + clipId + ")");
                var origin = frame.Viewport.ToScreen(image.Corners[3]);
                var x = frame.Viewport.ToScreen(image.Corners[2]) - origin;
                var y = frame.Viewport.ToScreen(image.Corners[0]) - origin;
                xml.WriteStartElement("image"); Attribute("width", 1); Attribute("height", 1);
                xml.WriteAttributeString("preserveAspectRatio", "none");
                xml.WriteAttributeString("transform", $"matrix({N(x.X)} {N(x.Y)} {N(y.X)} {N(y.Y)} {N(origin.X)} {N(origin.Y)})");
                xml.WriteAttributeString("href", data.ContentType.Insert(0, "data:") + ";base64," + Convert.ToBase64String(data.Data.Span));
                Attribute("opacity", (color >> 24) / 255.0);
                if (filter is not null) xml.WriteAttributeString("filter", "url(#" + filter + ")");
                xml.WriteEndElement(); xml.WriteEndElement();
            }
            string? WriteImageFilter(CadImage image)
            {
                if (!double.IsFinite(image.Brightness) || !double.IsFinite(image.Contrast) || !double.IsFinite(image.Fade))
                    throw new ArgumentException("Image display adjustments must be finite.");
                if (image.Brightness == 0.5 && image.Contrast == 0.5 && image.Fade == 0) return null;
                var contrast = Math.Clamp(image.Contrast, 0, 1) * 2; var fade = Math.Clamp(image.Fade, 0, 1);
                var gain = contrast * (1 - fade);
                var offset = (0.5 * (1 - contrast) + Math.Clamp(image.Brightness, 0, 1) - 0.5) * (1 - fade);
                var r = offset + fade * ((frame.Background >> 16) & 255) / 255.0;
                var g = offset + fade * ((frame.Background >> 8) & 255) / 255.0;
                var b = offset + fade * (frame.Background & 255) / 255.0;
                var id = "shadow-image-filter-" + nextId++;
                xml.WriteStartElement("defs"); xml.WriteStartElement("filter"); xml.WriteAttributeString("id", id);
                xml.WriteAttributeString("color-interpolation-filters", "sRGB");
                xml.WriteStartElement("feColorMatrix"); xml.WriteAttributeString("type", "matrix");
                xml.WriteAttributeString("values", $"{N(gain)} 0 0 0 {N(r)} 0 {N(gain)} 0 0 {N(g)} 0 0 {N(gain)} 0 {N(b)} 0 0 0 1 0");
                xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); return id;
            }
        }
        return new(stream.ToArray(), "image/svg+xml");
    }
    private static string N(double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("SVG coordinates must be finite.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
    private static string Color(uint argb) => "#" + (argb & 0xffffff).ToString("x6", CultureInfo.InvariantCulture);
}
