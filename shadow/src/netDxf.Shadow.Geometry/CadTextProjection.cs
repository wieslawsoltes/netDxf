namespace netDxf.Shadow.Geometry;

/// <summary>Effective text and its observed resource, independent of the native DXF model.</summary>
public sealed record CadTextSource(CadText Text, ShadowReader Style, Vec3 Normal, double Rotation,
    int DrawingDirection = 5, bool HasBackground = false);

/// <summary>Shared text interpretation for standard and external-font projectors.</summary>
public static class CadTextProjection
{
    private const double Degrees = Math.PI / 180;
    private static readonly string[] Attachments = ["TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter",
        "MiddleRight", "BottomLeft", "BottomCenter", "BottomRight"];

    /// <summary>Drawing presentation uses the ATTDEF tag; editing still reads the stored default.</summary>
    public static CadTextSource ReadDisplay(ShadowReader entity)
    {
        var source = Read(entity);
        return entity.Type == "AttributeDefinition" ? source with { Text = source.Text with { Content = entity["Tag"].Text } } : source;
    }

    public static CadTextSource Read(ShadowReader entity)
    {
        if (entity.Type is not ("Text" or "MText" or "Attribute" or "AttributeDefinition"))
            throw new ArgumentException("Choose a text entity or an attribute.");
        if (entity.Type is "Attribute" or "AttributeDefinition")
        {
            // The editable packet is authoritative in detached previews; derived
            // native HasMText metadata is reconciled when a plan reaches the source.
            var packet = entity["TextData"].Value;
            if (packet is ShadowValue.Structure stored && stored.TypeName != "netDxf.Entities.AttributeTextData"
                || packet is not (ShadowValue.Structure or ShadowValue.NullValue))
                throw new NotSupportedException("Attribute text data is unavailable or has an unsupported representation.");
            if (entity["TextData"]["Content"].Exists) return Embedded(entity);
            if (entity["TextData"]["Kind"].Number() is 2 or 4 || entity["HasMText"].Boolean()
                && entity.Object?.Properties.ContainsKey("TextData") != true)
                throw new NotSupportedException("Embedded attribute text requires a complete captured text packet.");
        }
        var multiline = entity.Type == "MText";
        var style = entity["Style"]; var normal = entity["Normal"].Vector(Vec3.UnitZ);
        var rotation = entity["Rotation"].Number();
        var axes = Affine3.ObjectCoordinates(normal) * Affine3.RotateZ(rotation * Degrees);
        var x = axes.X * entity["WidthFactor"].Number(1) * (entity["IsBackward"].Boolean() ? -1 : 1);
        var y = (axes.Y + axes.X * Math.Tan(entity["ObliqueAngle"].Number() * Degrees)) * (entity["IsUpsideDown"].Boolean() ? -1 : 1);
        var text = new CadText(entity["Value"].Text, entity["Position"].Vector(), x, y, entity["Height"].Number(1),
            Family(style), entity[multiline ? "AttachmentPoint" : "Alignment"].Text, multiline,
            entity["RectangleWidth"].Number(entity["Width"].Number()))
        {
            LineSpacing = multiline ? new(entity["LineSpacingFactor"].Number(1), entity["LineSpacingStyle"].Text is "Exact" or "Exactly") : null
        };
        var direction = !multiline ? 5 : entity["DrawingDirection"].Text switch { "LeftToRight" => 1, "TopToBottom" => 3, _ => 5 };
        return new(text, style, normal, rotation, direction, entity["BackgroundFill"].Exists);
    }

    private static CadTextSource Embedded(ShadowReader entity)
    {
        var content = entity["TextData"]["Content"];
        if (content.Value is not ShadowValue.Structure { TypeName: "netDxf.Entities.AttributeMText" }
            || content["Fields"].Value is not ShadowValue.Sequence sequence || sequence.Items.Length > 128)
            throw new NotSupportedException("Embedded attribute text requires a complete captured text packet.");
        var fields = new Dictionary<int, ShadowReader>(); var order = new List<int>();
        int[] supported = [10, 20, 30, 11, 21, 31, 210, 220, 230, 40, 41, 42, 43, 46, 50, 71, 72, 73, 44, 90, 45, 63, 421, 431, 441];
        foreach (var tag in content["Fields"].Items)
        {
            var rawCode = tag["Code"].Number(double.NaN);
            if (!double.IsFinite(rawCode) || rawCode != (int)rawCode || !supported.Contains((int)rawCode) || !fields.TryAdd((int)rawCode, tag["Value"]))
                throw new ArgumentException("Embedded text has invalid or duplicate stored fields.");
            order.Add((int)rawCode);
        }
        double Number(int code, double fallback)
        {
            var value = fields.TryGetValue(code, out var field) ? field.Number(double.NaN) : fallback;
            if (!double.IsFinite(value)) throw new ArgumentException("Embedded text coordinates and dimensions must be finite.");
            return value;
        }
        Vec3 Vector(int code, Vec3 fallback)
        {
            var count = new[] { code, code + 10, code + 20 }.Count(fields.ContainsKey);
            if (count == 0) return fallback;
            if (count != 3) throw new ArgumentException("Embedded text has an incomplete vector.");
            return new(Number(code, 0), Number(code + 10, 0), Number(code + 20, 0));
        }
        var normal = Vector(210, Vec3.UnitZ); var basis = Affine3.ObjectCoordinates(normal);
        var textDirection = Vector(11, Vec3.UnitX);
        if (textDirection.Length == 0) throw new ArgumentException("Embedded text direction must be nonzero.");
        var rotation = Number(50, 0);
        if (fields.ContainsKey(11) && (!fields.ContainsKey(50) || order.IndexOf(11) > order.IndexOf(50)))
        {
            rotation = Math.Atan2(Vec3.Dot(textDirection, basis.Y), Vec3.Dot(textDirection, basis.X)) / Degrees;
        }
        var axes = basis * Affine3.RotateZ(rotation * Degrees);
        var height = Number(40, 2.5); var width = Number(41, 0); var attachment = Number(71, 1);
        var factor = Number(44, 1); var spacing = Number(73, 1); var flow = Number(72, 5);
        if (height <= 0 || width < 0 || attachment != (int)attachment || attachment < 1 || attachment > 9
            || factor < .25 || factor > 4 || spacing is not (1 or 2) || flow is not (1 or 3 or 5))
            throw new ArgumentException("Embedded text has invalid layout parameters.");
        var style = content["Style"];
        var text = new CadText(content["Value"].Text, Vector(10, Vec3.Zero), axes.X, axes.Y, height,
            Family(style), Attachments[(int)attachment - 1], true, width) { LineSpacing = new(factor, spacing == 2) };
        return new(text, style, normal, rotation, (int)flow, fields.ContainsKey(90));
    }

    private static string Family(ShadowReader style) => style["FontFamilyName"].Text is { Length: > 0 } family ? family : "sans-serif";
}
