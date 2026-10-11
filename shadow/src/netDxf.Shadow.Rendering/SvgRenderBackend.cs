using System.Globalization;
using System.Text;
using System.Xml;

namespace netDxf.Shadow.Rendering;

/// <summary>A second runtime-swappable backend; no image/font URI from a DXF is fetched implicitly.</summary>
public sealed class SvgRenderBackend : IRenderBackend
{
    public string Id => "svg";
    public void Dispose() { }
    public RenderImage Render(RenderFrame frame, CancellationToken cancellationToken = default)
    {
        frame.Viewport.Validate();
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, OmitXmlDeclaration = true }))
        {
            xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            Attribute("width", frame.Viewport.Width); Attribute("height", frame.Viewport.Height);
            xml.WriteStartElement("rect"); Attribute("width", frame.Viewport.Width); Attribute("height", frame.Viewport.Height); xml.WriteAttributeString("fill", Color(frame.Background)); xml.WriteEndElement();
            foreach (var item in frame.VisibleItems())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var color = frame.Selection?.Contains(item.Key) == true ? frame.SelectionColor : item.Style.Argb;
                var path = new StringBuilder();
                foreach (var contour in item.Contours)
                {
                    for (var i = 0; i < contour.Points.Length; i++)
                    {
                        var p = frame.Viewport.ToScreen(contour.Points[i]);
                        path.Append(i == 0 ? 'M' : 'L').Append(N(p.X)).Append(' ').Append(N(p.Y));
                    }
                    if (contour.Closed) path.Append('Z');
                }
                if (item.Image is { } image)
                {
                    for (var i = 0; i < image.Corners.Length; i++)
                    {
                        var p = frame.Viewport.ToScreen(image.Corners[i]); path.Append(i == 0 ? 'M' : 'L').Append(N(p.X)).Append(' ').Append(N(p.Y));
                    }
                    path.Append('Z');
                }
                if (path.Length > 0)
                {
                    xml.WriteStartElement("path"); xml.WriteAttributeString("d", path.ToString());
                    xml.WriteAttributeString("fill", item.Filled ? Color(color) : "none"); xml.WriteAttributeString("fill-rule", "evenodd");
                    xml.WriteAttributeString("stroke", Color(color)); Attribute("stroke-width", Math.Max(1, item.Style.Lineweight * 96 / 25.4));
                    Attribute("opacity", (color >> 24) / 255.0); xml.WriteAttributeString("stroke-linecap", "round");
                    if (!item.Style.Dashes.IsDefaultOrEmpty) xml.WriteAttributeString("stroke-dasharray", string.Join(" ", item.Style.Dashes.Select(d => N(Math.Max(Math.Abs(d) * frame.Viewport.PixelsPerUnit, 1)))));
                    xml.WriteEndElement();
                }
                if (item.Text is { } text)
                {
                    var origin = frame.Viewport.ToScreen(text.Origin);
                    var scale = frame.Viewport.PixelsPerUnit;
                    xml.WriteStartElement("g");
                    xml.WriteAttributeString("transform", $"matrix({N(text.XAxis.X * scale)} {N(-text.XAxis.Y * scale)} {N(-text.YAxis.X * scale)} {N(text.YAxis.Y * scale)} {N(origin.X)} {N(origin.Y)})");
                    xml.WriteAttributeString("fill", Color(color)); Attribute("opacity", (color >> 24) / 255.0);
                    foreach (var line in TextLayout.Layout(text, s => s.Length * text.Height * 0.6))
                    {
                        xml.WriteStartElement("text"); Attribute("x", line.X); Attribute("y", line.Y); Attribute("font-size", text.Height);
                        xml.WriteAttributeString("font-family", text.FontFamily); xml.WriteString(line.Text); xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                }
            }
            xml.WriteEndElement();
            void Attribute(string name, double value) => xml.WriteAttributeString(name, N(value));
        }
        return new(stream.ToArray(), "image/svg+xml");
    }
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Color(uint argb) => "#" + (argb & 0xffffff).ToString("x6", CultureInfo.InvariantCulture);
}
