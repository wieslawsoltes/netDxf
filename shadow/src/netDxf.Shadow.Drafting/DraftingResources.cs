using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Drafting;

public sealed record ResourceEdit(string Kind, string Action, ObjectId? Id = null, LinetypeEdit? Linetype = null, TextStyleEdit? TextStyle = null);
public sealed record LinetypeSegmentEdit(string Kind, double Length, string? Text = null, string? Style = null,
    double OffsetX = 0, double OffsetY = 0, double Scale = 1, double Rotation = 0, string RotationType = "Relative", short? ShapeNumber = null);
public sealed record LinetypeEdit(string Name, string Description = "", ImmutableArray<LinetypeSegmentEdit> Segments = default);
public sealed record TextStyleEdit(string Name, string? FontFile = null, string? BigFont = null, string? FontFamily = null,
    string? FontStyle = null, double? Height = null, double? WidthFactor = null, double? ObliqueAngle = null,
    bool? Vertical = null, bool? Backward = null, bool? UpsideDown = null);
public sealed record DraftingResourceStatus(string Id, string Name, bool Current, bool Reserved, int Uses);
public sealed record DraftingLinetype(DraftingResourceStatus Status, string Description, ImmutableArray<LinetypeSegmentEdit> Segments);
public sealed record DraftingTextStyle(DraftingResourceStatus Status, string FontFile, string BigFont, string FontFamily,
    string FontStyle, double Height, double WidthFactor, double ObliqueAngle, bool Vertical, bool Backward, bool UpsideDown);

/// <summary>Bounded named-resource edits, detached from hosts and executed as one undoable source transaction.</summary>
public static class DraftingResources
{
    internal static (string Table, string Header) Names(string kind) => kind.ToLowerInvariant() switch
    {
        "layer" => ("Layers", "CLayer"), "linetype" => ("Linetypes", "CeLtype"), "textstyle" => ("TextStyles", "TextStyle"),
        _ => throw new ArgumentException("Choose layer, linetype, or textstyle.")
    };
    public static string Current(ShadowSnapshot snapshot, string kind) => snapshot.Document["DrawingVariables"][Names(kind).Header].Text;
    public static int Uses(ShadowSnapshot snapshot, ObjectId id)
    {
        var tables = new[] { "Layers", "Linetypes", "TextStyles", "ShapeStyles" }.Select(t => snapshot.Document[t].Id).ToHashSet();
        return snapshot.Objects.Values.Count(n => n.Id != snapshot.Root && n.Id != id && !tables.Contains(n.Id) && n.Properties.Values.Any(v => v.References().Contains(id)));
    }
    public static bool Reserved(ShadowReader item, string kind) => item["IsReserved"].Boolean() || kind.ToLowerInvariant() switch
    { "layer" => item["Name"].Text == "0", "linetype" => new[] { "ByLayer", "ByBlock", "Continuous" }.Contains(item["Name"].Text, StringComparer.OrdinalIgnoreCase), "textstyle" => item["Name"].Text.Equals("Standard", StringComparison.OrdinalIgnoreCase), _ => false };
    private static DraftingResourceStatus Status(ShadowSnapshot snapshot, ShadowReader item, string kind) => new(item.Id!.Value.Value, item["Name"].Text,
        item["Name"].Text.Equals(Current(snapshot, kind), StringComparison.OrdinalIgnoreCase), Reserved(item, kind), Uses(snapshot, item.Id.Value));
    public static ImmutableArray<DraftingLinetype> Linetypes(ShadowSnapshot snapshot) => snapshot.Document["Linetypes"].Items.Select(l => new DraftingLinetype(Status(snapshot, l, "linetype"), l["Description"].Text,
        l["Segments"].Items.Select(s => new LinetypeSegmentEdit(s.Type.Replace("Linetype", "").Replace("Segment", "").ToLowerInvariant(), s["Length"].Number(),
            s.Type == "LinetypeShapeSegment" ? s["Name"].Text : s["Text"].Text, s["Style"]["Name"].Text, s["Offset"]["X"].Number(), s["Offset"]["Y"].Number(),
            s["Scale"].Number(1), s["Rotation"].Number(), s["RotationType"].Exists ? s["RotationType"].Text : "Relative", s["ShapeNumber"].Exists ? (short)s["ShapeNumber"].Number() : null)).ToImmutableArray()))
        .OrderBy(l => l.Status.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    public static ImmutableArray<DraftingTextStyle> TextStyles(ShadowSnapshot snapshot) => snapshot.Document["TextStyles"].Items.Select(s => new DraftingTextStyle(Status(snapshot, s, "textstyle"),
        s["FontFile"].Text, s["BigFont"].Text, s["FontFamilyName"].Text, FontFace(s), s["Height"].Number(), s["WidthFactor"].Number(1), s["ObliqueAngle"].Number(),
        s["IsVertical"].Boolean(), s["IsBackward"].Boolean(), s["IsUpsideDown"].Boolean())).OrderBy(s => s.Status.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();

    private static string FontFace(ShadowReader style) => (((int)style["ExtendedFontData"]["Flags"].Number() >> 24) & 3) switch
        { 1 => "Italic", 2 => "Bold", 3 => "BoldItalic", _ => "Regular" };

    public static DraftingPlan Plan(ShadowSnapshot snapshot, ResourceEdit request)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentException.ThrowIfNullOrWhiteSpace(request.Kind); ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
        var kind = request.Kind.ToLowerInvariant(); var (table, header) = Names(kind); var items = snapshot.Document[table].Items.ToArray();
        var current = items.FirstOrDefault(x => x.Id == request.Id); var edit = new ShadowGraphEdit(snapshot);
        if (request.Action is "delete" or "current")
        {
            if (!current.Exists) throw new ArgumentException("Choose an existing resource.");
            if (request.Action == "current")
            {
                if (kind == "layer" && (current["IsFrozen"].Boolean() || !current["IsVisible"].Boolean(true))) throw new ArgumentException("Turn on and thaw the layer before making it current.");
                edit.Set(snapshot.Document["DrawingVariables"].Id!.Value, header, ShadowValue.From(current["Name"].Text));
            }
            else
            {
                if (Reserved(current, kind)) throw new ArgumentException("Reserved resources cannot be deleted.");
                if (current["Name"].Text.Equals(Current(snapshot, kind), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose another current resource before deleting this one.");
                if (Uses(snapshot, current.Id!.Value) != 0) throw new ArgumentException("This resource is in use. Reassign its references before deleting it.");
                edit.ReplaceSequence(snapshot.Root, table, items.Where(x => x.Id != current.Id).Select(x => x.Value));
                edit.PruneUnreachable();
            }
        }
        else if (request.Action == "save" && kind is "linetype" or "textstyle")
        {
            if (request.Id is not null && !current.Exists) throw new ArgumentException("Unknown resource.");
            var name = kind == "linetype" ? request.Linetype?.Name : request.TextStyle?.Name;
            ValidateName(name, items, request.Id);
            if (current.Exists && Reserved(current, kind) && !string.Equals(current["Name"].Text, name, StringComparison.Ordinal)) throw new ArgumentException("Reserved resources cannot be renamed.");
            if (kind == "linetype" && current.Exists && Reserved(current, kind)) throw new ArgumentException("Built-in linetypes cannot be redefined. Create a named copy.");
            var id = request.Id ?? edit.Create("netDxf.Tables." + (kind == "linetype" ? "Linetype" : "TextStyle"), ShadowObjectKind.Table,
                kind == "linetype" ? new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(name!), ["Description"] = ShadowValue.From(""), ["Segments"] = new ShadowValue.Sequence([]) } :
                new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(name!), ["FontFile"] = ShadowValue.From("txt.shx"), ["BigFont"] = ShadowValue.From(""), ["ExtendedFontData"] = ShadowValue.Null });
            if (request.Id is null) edit.ReplaceSequence(snapshot.Root, table, items.Select(x => x.Value).Append(new ShadowValue.Reference(id)));
            edit.Set(id, "Name", ShadowValue.From(name!));
            if (current.Exists && current["Name"].Text.Equals(Current(snapshot, kind), StringComparison.OrdinalIgnoreCase)) edit.Set(snapshot.Document["DrawingVariables"].Id!.Value, header, ShadowValue.From(name!));
            if (kind == "linetype") SaveLinetype(snapshot, edit, id, request.Linetype!); else SaveTextStyle(edit, id, current, request.TextStyle!);
            edit.PruneUnreachable();
        }
        else throw new ArgumentException("Use save, current, or delete; layer properties use the layer editor.");
        return new($"{request.Action.ToUpperInvariant()} {kind.ToUpperInvariant()}", edit.Build(), []);
    }

    internal static void ValidateName(string? name, IEnumerable<ShadowReader> items, ObjectId? id)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(c => char.IsControl(c) || "<>/\\\":;?*|,=`".Contains(c))) throw new ArgumentException("Names require 1–255 printable characters without CAD reserved separators.");
        if (items.Any(x => x.Id != id && x["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Resource names must be unique, ignoring case.");
    }
    internal static void Finite(double value, double min, double max, string name)
    { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{name} must be between {min} and {max}."); }
    private static void SaveLinetype(ShadowSnapshot snapshot, ShadowGraphEdit edit, ObjectId id, LinetypeEdit value)
    {
        if (value.Description is null || value.Description.Length > 1024) throw new ArgumentException("Linetype descriptions are limited to 1024 characters.");
        if (!value.Segments.IsDefault && value.Segments.Length > 256) throw new ArgumentException("A linetype may contain at most 256 segments.");
        var segments = new List<ShadowValue>();
        foreach (var segment in value.Segments.IsDefault ? [] : value.Segments)
        {
            Finite(segment.Length, -1e9, 1e9, "Segment length");
            var properties = new Dictionary<string, ShadowValue> { ["Length"] = ShadowValue.From(segment.Length) };
            var type = segment.Kind.ToLowerInvariant() switch { "simple" => "LinetypeSimpleSegment", "text" => "LinetypeTextSegment", "shape" => "LinetypeShapeSegment", _ => throw new ArgumentException("Segment kind must be simple, text, or shape.") };
            if (type != "LinetypeSimpleSegment")
            {
                Finite(segment.OffsetX, -1e9, 1e9, "Offset X"); Finite(segment.OffsetY, -1e9, 1e9, "Offset Y"); Finite(segment.Scale, 1e-9, 1e9, "Segment scale"); Finite(segment.Rotation, -1e9, 1e9, "Rotation");
                if (segment.RotationType is not ("Relative" or "Absolute" or "Upright")) throw new ArgumentException("Unknown segment rotation mode.");
                if ((segment.Text?.Length ?? 0) > 1024 || type == "LinetypeShapeSegment" && (string.IsNullOrEmpty(segment.Text) || segment.ShapeNumber is null)) throw new ArgumentException("Shape segments require a name and numeric shape identifier; text is limited to 1024 characters.");
                var style = snapshot.Document[type == "LinetypeTextSegment" ? "TextStyles" : "ShapeStyles"].Items.FirstOrDefault(s => s["Name"].Text.Equals(segment.Style, StringComparison.OrdinalIgnoreCase));
                if (!style.Exists) throw new ArgumentException("Choose an existing document text or shape style for each complex segment.");
                properties[type == "LinetypeTextSegment" ? "Text" : "Name"] = ShadowValue.From(segment.Text ?? ""); properties["Style"] = style.Value;
                properties["Offset"] = ShadowValue.From(new Vec3(segment.OffsetX, segment.OffsetY, 0), true); properties["Scale"] = ShadowValue.From(segment.Scale);
                properties["Rotation"] = ShadowValue.From((segment.Rotation % 360 + 360) % 360); properties["RotationType"] = new ShadowValue.Atom("netDxf.Tables.LinetypeSegmentRotationType", segment.RotationType);
                if (type == "LinetypeShapeSegment") properties["ShapeNumber"] = new ShadowValue.Atom("System.Int16", segment.ShapeNumber!.Value.ToString(CultureInfo.InvariantCulture));
            }
            segments.Add(new ShadowValue.Reference(edit.Create("netDxf.Tables." + type, ShadowObjectKind.Object, properties)));
        }
        edit.Set(id, "Description", ShadowValue.From(value.Description)); edit.ReplaceSequence(id, "Segments", segments);
    }
    private static void SaveTextStyle(ShadowGraphEdit edit, ObjectId id, ShadowReader current, TextStyleEdit value)
    {
        var fontChange = value.FontFile is not null || value.BigFont is not null || value.FontFamily is not null || value.FontStyle is not null;
        if (fontChange)
        {
            var file = value.FontFile ?? current["FontFile"].Text; var big = value.BigFont ?? current["BigFont"].Text; var family = value.FontFamily ?? current["FontFamilyName"].Text;
            if (file.Length > 1024 || big.Length > 1024 || family.Length > 255 || file.Any(char.IsControl) || big.Any(char.IsControl) || family.Any(char.IsControl)) throw new ArgumentException("Font references must be bounded printable resource names.");
            if (file.Length != 0 && !(file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Use a TTF or SHX font reference.");
            if (file.Length == 0 && string.IsNullOrWhiteSpace(family)) throw new ArgumentException("Choose a font file or a TrueType family.");
            if (big.Length != 0 && (!file.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || !big.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Big fonts require an SHX primary font and SHX big font.");
            var flags = value.FontStyle ?? FontFace(current);
            var bits = flags switch { "" or "Regular" => 0, "Bold" => 2, "Italic" => 1, "Bold, Italic" or "Italic, Bold" or "BoldItalic" => 3, _ => throw new ArgumentException("Choose Regular, Bold, Italic, or BoldItalic.") };
            var oldBits = (int)current["ExtendedFontData"]["Flags"].Number();
            var extended = family.Length == 0 ? ShadowValue.Null : new ShadowValue.Structure("netDxf.Tables.TextStyleFontData", ImmutableDictionary<string, ShadowValue>.Empty
                .Add("FamilyName", ShadowValue.From(family)).Add("Flags", new ShadowValue.Atom("System.Int32", ((oldBits & ~0x03000000) | bits << 24).ToString(CultureInfo.InvariantCulture))));
            edit.Set(id, "FontFile", ShadowValue.From(file)); edit.Set(id, "BigFont", ShadowValue.From(big)); edit.Set(id, "ExtendedFontData", extended);
        }
        foreach (var (name, number, min, max) in new[] { ("Height", value.Height, 0d, 1e9), ("WidthFactor", value.WidthFactor, .01, 100d), ("ObliqueAngle", value.ObliqueAngle, -85d, 85d) })
            if (number is { } n) { Finite(n, min, max, name); edit.Set(id, name, ShadowValue.From(n)); }
        foreach (var (name, flag) in new[] { ("IsVertical", value.Vertical), ("IsBackward", value.Backward), ("IsUpsideDown", value.UpsideDown) })
            if (flag is { } b) edit.Set(id, name, ShadowValue.From(b));
    }
}
