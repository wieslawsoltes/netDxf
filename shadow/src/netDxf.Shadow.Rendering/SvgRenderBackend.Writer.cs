using System.Globalization;
using System.Text;
using System.Xml;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public sealed partial class SvgRenderBackend
{
    private sealed class Writer(RenderFrame frame, XmlWriter xml, ISvgImageResolver? images)
    {
        private int nextId;
        public void Attribute(string name, double value) => xml.WriteAttributeString(name, N(value));
        private string PathData(IEnumerable<RenderContour> contours)
        {
            var path = new StringBuilder();
            foreach (var contour in contours)
            {
                for (var i = 0; i < contour.Points.Length; i++)
                {
                    var p = frame.Viewport.ToScreen(contour.Points[i]);
                    path.Append(i == 0 ? 'M' : 'L').Append(N(p.X)).Append(' ').Append(N(p.Y));
                }
                if (contour.Closed) path.Append('Z');
            }
            return path.ToString();
        }
        public void Path(IEnumerable<RenderContour> contours, string fill, string? stroke, CadFillRule rule, uint color, double width)
        {
            var data = PathData(contours); if (data.Length == 0) return;
            xml.WriteStartElement("path"); xml.WriteAttributeString("d", data); xml.WriteAttributeString("fill", fill);
            xml.WriteAttributeString("fill-rule", rule == CadFillRule.NonZero ? "nonzero" : "evenodd");
            if (stroke is not null)
            {
                xml.WriteAttributeString("stroke", stroke); Attribute("stroke-width", width);
                xml.WriteAttributeString("stroke-linecap", "round"); xml.WriteAttributeString("stroke-linejoin", "round");
            }
            Attribute("opacity", (color >> 24) / 255.0); xml.WriteEndElement();
        }
        public string Gradient(CadGradient gradient)
        {
            var id = "shadow-gradient-" + nextId++; var plan = GradientPlan.Create(gradient);
            xml.WriteStartElement("defs"); xml.WriteStartElement(plan.Radial ? "radialGradient" : "linearGradient");
            xml.WriteAttributeString("id", id); xml.WriteAttributeString("gradientUnits", "userSpaceOnUse"); xml.WriteAttributeString("color-interpolation", "sRGB");
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
        public void Text(CadText text, RichTextLayoutResult layout, uint color, bool selected)
        {
            var origin = frame.Viewport.ToScreen(text.Origin); var scale = frame.Viewport.PixelsPerUnit;
            xml.WriteStartElement("g");
            xml.WriteAttributeString("transform", $"matrix({N(text.XAxis.X * scale)} {N(-text.XAxis.Y * scale)} {N(-text.YAxis.X * scale)} {N(text.YAxis.Y * scale)} {N(origin.X)} {N(origin.Y)})");
            foreach (var run in layout.Runs)
            {
                var paint = Resolve(run.Style.Color);
                xml.WriteStartElement("text"); Attribute("font-size", run.Style.Height);
                xml.WriteAttributeString("transform", $"matrix({N(run.Style.WidthFactor)} 0 {N(-Math.Tan(run.Style.ObliqueDegrees * Math.PI / 180))} 1 {N(run.X)} {N(run.Y)})");
                xml.WriteAttributeString("font-family", run.Style.FontFamily);
                xml.WriteAttributeString("font-weight", run.Style.Bold ? "700" : "400"); xml.WriteAttributeString("font-style", run.Style.Italic ? "italic" : "normal");
                xml.WriteAttributeString("fill", Color(paint)); Attribute("opacity", (paint >> 24) / 255.0);
                xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                if (run.Advance > 0) { Attribute("textLength", run.Advance / run.Style.WidthFactor); xml.WriteAttributeString("lengthAdjust", "spacingAndGlyphs"); }
                xml.WriteString(run.Text); xml.WriteEndElement();
            }
            foreach (var rule in layout.Rules)
            {
                var paint = Resolve(rule.Color);
                xml.WriteStartElement("line"); Attribute("x1", rule.X1); Attribute("y1", rule.Y1); Attribute("x2", rule.X2); Attribute("y2", rule.Y2);
                xml.WriteAttributeString("stroke", Color(paint)); Attribute("stroke-width", rule.Thickness); Attribute("opacity", (paint >> 24) / 255.0); xml.WriteEndElement();
            }
            xml.WriteEndElement();
            uint Resolve(uint? authored) => selected || authored is null ? color :
                (authored.Value & 0xffffffu) | ((authored.Value >> 24) * (color >> 24) / 255) << 24;
        }
        public void Image(CadImage image, bool selected, uint color, double width)
        {
            if (image.Corners.Length != 4) return;
            var data = selected ? null : images?.Resolve(image.FileName);
            if (data is null)
            {
                var points = !image.ClipOutside && !image.ClipPolygon.IsEmpty ? image.ClipPolygon : image.Corners;
                Path([new RenderContour(points, true)], "none", Color(color), CadFillRule.EvenOdd, color, width); return;
            }
            if (data.ContentType is not ("image/png" or "image/jpeg" or "image/webp"))
                throw new ArgumentException("The SVG image resolver may supply PNG, JPEG or WebP raster bytes only.");
            if (data.Data.IsEmpty || data.Data.Length > 16 * 1024 * 1024) throw new ArgumentException("Embedded image byte budget exceeded.");
            string? clipId = null;
            if (!image.ClipPolygon.IsDefaultOrEmpty)
            {
                clipId = "shadow-image-clip-" + nextId++;
                xml.WriteStartElement("defs"); xml.WriteStartElement("clipPath"); xml.WriteAttributeString("id", clipId); xml.WriteAttributeString("clipPathUnits", "userSpaceOnUse");
                xml.WriteStartElement("path");
                var contours = image.ClipOutside
                    ? new[] { new RenderContour(image.Corners, true), new RenderContour(image.ClipPolygon, true) }
                    : new[] { new RenderContour(image.ClipPolygon, true) };
                xml.WriteAttributeString("d", PathData(contours)); xml.WriteAttributeString("clip-rule", "evenodd");
                xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
            }
            var filter = ImageFilter(image);
            xml.WriteStartElement("g"); if (clipId is not null) xml.WriteAttributeString("clip-path", "url(#" + clipId + ")");
            var origin = frame.Viewport.ToScreen(image.Corners[3]); var x = frame.Viewport.ToScreen(image.Corners[2]) - origin; var y = frame.Viewport.ToScreen(image.Corners[0]) - origin;
            xml.WriteStartElement("image"); Attribute("width", 1); Attribute("height", 1); xml.WriteAttributeString("preserveAspectRatio", "none");
            xml.WriteAttributeString("transform", $"matrix({N(x.X)} {N(x.Y)} {N(y.X)} {N(y.Y)} {N(origin.X)} {N(origin.Y)})");
            xml.WriteAttributeString("href", "data:" + data.ContentType + ";base64," + Convert.ToBase64String(data.Data.Span)); Attribute("opacity", (color >> 24) / 255.0);
            if (filter is not null) xml.WriteAttributeString("filter", "url(#" + filter + ")"); xml.WriteEndElement(); xml.WriteEndElement();
        }
        private string? ImageFilter(CadImage image)
        {
            if (!double.IsFinite(image.Brightness) || !double.IsFinite(image.Contrast) || !double.IsFinite(image.Fade))
                throw new ArgumentException("Image display adjustments must be finite.");
            if (image.Brightness == 0.5 && image.Contrast == 0.5 && image.Fade == 0) return null;
            var contrast = Math.Clamp(image.Contrast, 0, 1) * 2; var fade = Math.Clamp(image.Fade, 0, 1); var gain = contrast * (1 - fade);
            var offset = (0.5 * (1 - contrast) + Math.Clamp(image.Brightness, 0, 1) - 0.5) * (1 - fade);
            var r = offset + fade * ((frame.Background >> 16) & 255) / 255.0; var g = offset + fade * ((frame.Background >> 8) & 255) / 255.0; var b = offset + fade * (frame.Background & 255) / 255.0;
            var id = "shadow-image-filter-" + nextId++;
            xml.WriteStartElement("defs"); xml.WriteStartElement("filter"); xml.WriteAttributeString("id", id); xml.WriteAttributeString("color-interpolation-filters", "sRGB");
            xml.WriteStartElement("feColorMatrix"); xml.WriteAttributeString("type", "matrix");
            xml.WriteAttributeString("values", $"{N(gain)} 0 0 0 {N(r)} 0 {N(gain)} 0 0 {N(g)} 0 0 {N(gain)} 0 {N(b)} 0 0 0 1 0");
            xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); return id;
        }
        private static string N(double value)
        {
            if (!double.IsFinite(value)) throw new ArithmeticException("SVG coordinates must be finite.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
        public static string Color(uint argb) => "#" + (argb & 0xffffff).ToString("x6", CultureInfo.InvariantCulture);
    }
}
