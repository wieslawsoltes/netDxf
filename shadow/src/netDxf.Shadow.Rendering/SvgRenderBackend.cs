using System.Text;
using System.Xml;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Only explicitly supplied raster bytes are embedded; DXF paths never trigger I/O.</summary>
public sealed record SvgImageData(string ContentType, ReadOnlyMemory<byte> Data);
public interface ISvgImageResolver { SvgImageData? Resolve(string reference); }

/// <summary>Runtime-swappable vector backend sharing compiled geometry and text layout.</summary>
public sealed partial class SvgRenderBackend : IRenderBackend
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
            var writer = new Writer(frame, xml, images);
            var clips = new Dictionary<RenderClip, string>(ReferenceEqualityComparer.Instance);
            xml.WriteStartElement("svg", "http://www.w3.org/2000/svg");
            writer.Attribute("width", frame.Viewport.Width); writer.Attribute("height", frame.Viewport.Height);
            xml.WriteAttributeString("viewBox", $"0 0 {frame.Viewport.Width} {frame.Viewport.Height}"); xml.WriteAttributeString("overflow", "hidden");
            xml.WriteStartElement("rect"); writer.Attribute("width", frame.Viewport.Width); writer.Attribute("height", frame.Viewport.Height);
            xml.WriteAttributeString("fill", Writer.Color(frame.Background)); xml.WriteEndElement();
            foreach (var item in frame.VisibleItems())
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var clip in item.Clips) BeginClip(clip);
                var selected = frame.Selection?.Contains(item.Key) == true; var color = selected ? frame.SelectionColor : item.Style.Argb;
                foreach (var region in item.Regions)
                {
                    var fill = Writer.Color(color);
                    if (!selected && region.Gradient is { } gradient) fill = "url(#" + writer.Gradient(gradient) + ")";
                    writer.Path(region.Contours, fill, null, region.FillRule, color, 0);
                }
                if (item.Filled) writer.Path(item.Contours, Writer.Color(color), null, CadFillRule.EvenOdd, color, 0);
                var stroke = Math.Max(1, item.Style.Lineweight * 96 / 25.4); var contours = StrokePattern.GetContours(item);
                writer.Path(contours, "none", Writer.Color(color), CadFillRule.EvenOdd, color, stroke);
                foreach (var contour in contours)
                {
                    if (contour.Points.Length != 2 || contour.Points[0] != contour.Points[1]) continue;
                    var p = frame.Viewport.ToScreen(contour.Points[0]);
                    xml.WriteStartElement("circle"); writer.Attribute("cx", p.X); writer.Attribute("cy", p.Y); writer.Attribute("r", Math.Max(0.5, stroke / 2));
                    xml.WriteAttributeString("fill", Writer.Color(color)); writer.Attribute("opacity", (color >> 24) / 255.0); xml.WriteEndElement();
                }
                if (item.Image is { Visible: true } image)
                {
                    if (item.ImageClip is { } clip) BeginClip(clip);
                    writer.Image(image, selected, color, stroke);
                    if (item.ImageClip is not null) xml.WriteEndElement();
                }
                if (item.Text is { Height: > 0 } text)
                {
                    if (item.TextClip is { } clip) BeginClip(clip);
                    writer.Text(text, item.TextLayout ?? ApproximateTextLayoutEngine.Instance.Layout(text), color, selected);
                    if (item.TextClip is not null) xml.WriteEndElement();
                }
                foreach (var _ in item.Clips) xml.WriteEndElement();
            }
            xml.WriteEndElement();
            void BeginClip(RenderClip clip)
            {
                if (!clips.TryGetValue(clip, out var id))
                {
                    id = "shadow-viewport-clip-" + clips.Count; clips.Add(clip, id);
                    xml.WriteStartElement("defs"); xml.WriteStartElement("clipPath");
                    xml.WriteAttributeString("id", id); xml.WriteAttributeString("clipPathUnits", "userSpaceOnUse");
                    xml.WriteAttributeString("clip-rule", "evenodd");
                    writer.Path(clip.Contours, "#000000", null, CadFillRule.EvenOdd, 0xff000000, 0);
                    xml.WriteEndElement(); xml.WriteEndElement();
                }
                xml.WriteStartElement("g"); xml.WriteAttributeString("clip-path", "url(#" + id + ")");
            }
        }
        return new(stream.ToArray(), "image/svg+xml");
    }
}
