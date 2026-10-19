using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf.Shadow.Rendering;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    /// <summary>Canonical source semantics and parsed SVG geometry, without capture IDs, handles, timestamps or XML IDs.</summary>
    internal JsonElement WorkflowObservation()
    {
        lock (gate)
        {
            var snapshot = Current; long work = 0;
            void Tick(int depth) { if (++work > 2_000_000 || depth > 64) throw new InvalidOperationException("Workflow observation budget exceeded."); }
            object? Value(ShadowValue value, HashSet<ObjectId> visited, int depth)
            {
                Tick(depth);
                return value switch
                {
                    ShadowValue.NullValue => null,
                    ShadowValue.Atom a => new { type = a.TypeName, value = a.Text },
                    ShadowValue.Fault f => new { fault = f.Message },
                    ShadowValue.Structure s => s.Fields.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => Value(p.Value, visited, depth + 1)),
                    ShadowValue.Sequence s => s.Items.Select(v => Value(v, visited, depth + 1)).ToArray(),
                    ShadowValue.Reference r => Reference(r.Id, visited, depth + 1),
                    _ => throw new NotSupportedException("Unknown workflow source value.")
                };
            }
            object Reference(ObjectId id, HashSet<ObjectId> visited, int depth)
            {
                Tick(depth); var reader = snapshot.Read(id); var node = reader.Object ?? throw new InvalidOperationException("Workflow source contains a dangling reference.");
                if (node.Kind.HasFlag(ShadowObjectKind.Block) || node.Type is "Layer" or "Linetype" or "TextStyle" or "ShapeStyle" or "ApplicationRegistry" or "DimensionStyle")
                    return new { type = node.TypeName, name = reader["Name"].Text };
                if (!visited.Add(id)) return new { cycle = node.TypeName };
                try { return Node(node, visited, depth + 1); } finally { visited.Remove(id); }
            }
            object Node(ShadowObject node, HashSet<ObjectId> visited, int depth)
            {
                Tick(depth);
                return new { type = node.TypeName, properties = node.Properties.Where(p => p.Key is not ("Handle" or "Owner" or "CodeName" or "HandleSeed"))
                    .OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => Value(p.Value, visited, depth + 1)) };
            }
            var blocks = snapshot.Document["Blocks"].Items.OrderBy(b => b["Name"].Text, StringComparer.Ordinal).Select(b => new
            {
                name = b["Name"].Text, origin = b["Origin"].Vector(), units = b["Record"]["Units"].Text,
                entities = b["Entities"].Items.Select(e => Node(e.Object!, [e.Id!.Value], 0)).ToArray(),
                attributes = Value(b["AttributeDefinitions"].Value, [], 0)
            }).ToArray();
            var resources = new[] { "Layers", "Linetypes", "TextStyles", "ShapeStyles", "DimensionStyles" }.ToDictionary(table => table,
                table => snapshot.Document[table].Items.OrderBy(r => r["Name"].Text, StringComparer.Ordinal).Select(r => Node(r.Object!, [r.Id!.Value], 0)).ToArray());
            var semanticBytes = JsonSerializer.SerializeToUtf8Bytes(new { blocks, resources }, StudioJson.CreateOptions());
            if (semanticBytes.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Workflow semantic observation exceeds 16 MiB.");
            var scene = Scene(); var viewport = Viewport.Fit(scene.Bounds, 640, 400);
            using var svg = new SvgRenderBackend(assets); var rendered = svg.Render(new(scene, viewport), default);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(rendered.Bytes));
            var geometry = xml.Descendants().Where(e => e.Name.LocalName is "path" or "line" or "circle" or "ellipse" or "rect" or "polyline" or "polygon" or "text" or "image")
                .Select(e => new { element = e.Name.LocalName, attributes = e.Attributes().Where(a => a.Name.LocalName is not ("id" or "clip-path" or "href"))
                    .OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToDictionary(a => a.Name.ToString(), a => a.Value), text = e.Name.LocalName == "text" ? e.Value : null }).ToArray();
            var selected = CadSelected(scene).Select(WorkflowReference).Order(StringComparer.Ordinal).ToArray();
            var entityTypes = snapshot.EntityIds.Select(snapshot.Read).GroupBy(e => e.Type).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
            return JsonSerializer.SerializeToElement(new
            {
                geometryHash = WorkflowHash(semanticBytes), renderGeometryHash = WorkflowHash(JsonSerializer.SerializeToUtf8Bytes(geometry, StudioJson.CreateOptions())),
                entityTypes, occurrences = scene.Items.Length, bounds = scene.Bounds.IsEmpty ? (Bounds2?)null : scene.Bounds,
                layout, workspace = activeBranch?.Name ?? "live", selection = selected,
                currentLayer = snapshot.Document["DrawingVariables"]["CLayer"].Text,
                currentLinetype = snapshot.Document["DrawingVariables"]["CeLtype"].Text,
                currentTextStyle = snapshot.Document["DrawingVariables"]["TextStyle"].Text
            }, StudioJson.CreateOptions());
        }
    }
}
