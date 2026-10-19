using System.Collections.Immutable;
using System.Globalization;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

public sealed record DraftingAttribute(ObjectId Id, string Tag, string Prompt, string Value, bool Multiline, bool Constant,
    bool DuplicateTag, string TextStyle, string Alignment, double Height, double Rotation, double Width, double WidthFactor,
    double ObliqueAngle, bool Backward, bool UpsideDown, string Layer, string Linetype, string Lineweight, string Color,
    bool Invisible, bool PositionLocked, double LineSpacing, bool ExactSpacing);
public sealed record AttributeEdit(ObjectId Attribute, string? Value = null, string? TextStyle = null, string? Alignment = null,
    double? Height = null, double? Rotation = null, double? Width = null, double? WidthFactor = null, double? ObliqueAngle = null,
    bool? Backward = null, bool? UpsideDown = null, string? Layer = null, string? Linetype = null, string? Lineweight = null,
    string? Color = null, bool? Invisible = null, bool? PositionLocked = null, double? LineSpacing = null, bool? ExactSpacing = null);

/// <summary>Instance attribute editing as immutable, previewable, single-undo plans.</summary>
public static class DraftingAttributes
{
    public static readonly ImmutableArray<string> Alignments = ["TopLeft", "TopCenter", "TopRight", "MiddleLeft", "MiddleCenter", "MiddleRight",
        "BottomLeft", "BottomCenter", "BottomRight", "BaselineLeft", "BaselineCenter", "BaselineRight", "Aligned", "Middle", "Fit"];
    public static ImmutableArray<DraftingAttribute> Read(ShadowSnapshot snapshot, ObjectId insert)
    {
        var root = Root(snapshot, insert, false); var attributes = root["Attributes"].Items.Take(10_001).ToArray();
        if (attributes.Length > 10_000) throw new ArgumentException("Attribute editor budget exceeded.");
        var duplicates = attributes.GroupBy(a => a["Tag"].Text, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return attributes.Select(a =>
        {
            var source = CadTextProjection.Read(a); var text = source.Text; var flags = Flags(a);
            return new DraftingAttribute(a.Id!.Value, a["Tag"].Text, a["Definition"]["Prompt"].Text, text.Content, text.Multiline,
                (flags & 2) != 0, duplicates.Contains(a["Tag"].Text), source.Style["Name"].Text, text.Alignment, text.Height,
                source.Rotation, text.Width, a["WidthFactor"].Number(1), a["ObliqueAngle"].Number(), a["IsBackward"].Boolean(),
                a["IsUpsideDown"].Boolean(), a["Layer"]["Name"].Text, a["Linetype"]["Name"].Text, a["Lineweight"].Text,
                Color(a), (flags & 1) != 0, a["IsPositionLocked"].Boolean(), text.LineSpacing?.Factor ?? 1, text.LineSpacing?.Exact ?? false);
        }).ToImmutableArray();
    }

    public static DraftingPlan Plan(ShadowSnapshot snapshot, ObjectId insert, ImmutableArray<AttributeEdit> requests, CancellationToken token = default)
    {
        var root = Root(snapshot, insert, true);
        if (requests.IsDefaultOrEmpty || requests.Length > 10_000) throw new ArgumentException("Choose a bounded nonempty attribute edit batch.");
        var owned = root["Attributes"].Items.Select(a => a.Id).ToHashSet(); var seen = new HashSet<ObjectId>();
        var edit = new ShadowGraphEdit(snapshot); var anyVisual = false;
        foreach (var request in requests)
        {
            token.ThrowIfCancellationRequested();
            if (request is null || !seen.Add(request.Attribute) || !owned.Contains(request.Attribute))
                throw new ArgumentException("Each edited attribute must be a unique member of the selected INSERT.");
            var attribute = snapshot.Read(request.Attribute);
            if (attribute.Type != "Attribute" || attribute["Owner"].Id != insert) throw new ArgumentException("Attribute ownership is inconsistent.");
            if (attribute["Layer"]["IsLocked"].Boolean()) throw new InvalidOperationException("An attribute is on a locked layer.");
            var source = CadTextProjection.Read(attribute); var multiline = source.Text.Multiline; var visual = false;
            var packet = attribute["TextData"].Value as ShadowValue.Structure;
            var content = packet?.Fields["Content"] as ShadowValue.Structure;
            var originalPacket = packet;
            void Set(string name, ShadowValue value, bool visible = true)
            {
                if (ShadowValue.Equivalent(attribute[name].Value, value)) return;
                edit.Set(request.Attribute, name, value); visual |= visible;
            }
            void TextField(string name, ShadowValue value)
            {
                if (ShadowValue.Equivalent(content!.Fields[name], value)) return;
                content = content with { Fields = content.Fields.SetItem(name, value) }; visual = true;
            }
            void Scalar(int code, double value, bool integer = false)
            {
                var fields = (ShadowValue.Sequence)content!.Fields["Fields"];
                var list = fields.Items.ToList(); var index = list.FindIndex(v => v is ShadowValue.Structure t && t.Fields["Code"] is ShadowValue.Atom a && a.Text == code.ToString(CultureInfo.InvariantCulture));
                var next = new ShadowValue.Structure("netDxf.IO.DxfTag", new Dictionary<string, ShadowValue>
                { ["Code"] = Atom("System.Int16", code), ["Value"] = integer ? Atom("System.Int16", value) : ShadowValue.From(value) }.ToImmutableDictionary());
                if (index >= 0) list[index] = next; else list.Add(next);
                TextField("Fields", new ShadowValue.Sequence(list.ToImmutableArray()));
            }
            void Number(string name, double? value, double minimum, double maximum, int? code = null)
            {
                if (value is not { } number) return;
                if (!double.IsFinite(number) || number < minimum || number > maximum) throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
                if (multiline && code is { } group) Scalar(group, number); else Set(name, ShadowValue.From(number));
            }
            if (request.Value is { } value)
            {
                if (value.Length > 1_000_000 || value.Contains('\0')) throw new ArgumentException("Attribute values require at most one million characters without NUL.");
                if (!multiline && (value.Contains('\r') || value.Contains('\n'))) throw new ArgumentException("Single-line attribute values cannot contain line breaks.");
                if ((Flags(attribute) & 2) != 0 && value != source.Text.Content) throw new InvalidOperationException("Edit constant attribute values in their block definition.");
                if (multiline) TextField("Value", ShadowValue.From(value)); else Set("Value", ShadowValue.From(value));
            }
            if (request.TextStyle is { } styleName)
            {
                var style = Resource(snapshot, "TextStyles", styleName);
                if (multiline) TextField("Style", style.Value); else Set("Style", style.Value);
            }
            if (request.Alignment is { } alignment)
            {
                int index = Alignments.IndexOf(alignment);
                if (index < 0 || multiline && index > 8) throw new ArgumentException("Unknown text justification for this attribute type.");
                if (multiline) Scalar(71, index + 1, true); else Set("Alignment", new ShadowValue.Atom("netDxf.Entities.TextAlignment", alignment));
            }
            Number("Height", request.Height, 1e-9, 1e9, 40); Number("Width", request.Width, 0, 1e12, 41);
            if (request.Rotation is { } rotation)
            {
                if (!double.IsFinite(rotation) || Math.Abs(rotation) > 1e9) throw new ArgumentException("Rotation must be finite and bounded.");
                if (multiline && Math.Abs(rotation - source.Rotation) > 1e-12)
                {
                    // Rotation becomes the authored representation; remove the old direction so field order cannot override it.
                    var fields = (ShadowValue.Sequence)content!.Fields["Fields"];
                    TextField("Fields", new ShadowValue.Sequence(fields.Items.Where(v => v is not ShadowValue.Structure t ||
                        t.Fields["Code"] is not ShadowValue.Atom a || a.Text is not ("11" or "21" or "31")).ToImmutableArray()));
                    Scalar(50, rotation);
                }
                else if (!multiline) Set("Rotation", ShadowValue.From(rotation));
            }
            if (multiline && (request.WidthFactor is not null || request.ObliqueAngle is not null || request.Backward is not null || request.UpsideDown is not null))
                throw new ArgumentException("Multiline attributes use inline formatting rather than single-line width, oblique, and mirror controls.");
            Number("WidthFactor", request.WidthFactor, .01, 100); Number("ObliqueAngle", request.ObliqueAngle, -85, 85);
            if (request.Backward is { } backward) Set("IsBackward", ShadowValue.From(backward));
            if (request.UpsideDown is { } upsideDown) Set("IsUpsideDown", ShadowValue.From(upsideDown));
            if (request.LineSpacing is not null || request.ExactSpacing is not null)
            {
                if (!multiline) throw new ArgumentException("Line spacing applies to multiline attributes.");
                Number("LineSpacingFactor", request.LineSpacing, .25, 4, 44);
                if (request.ExactSpacing is { } exact) Scalar(73, exact ? 2 : 1, true);
            }
            if (request.Layer is { } layerName)
            {
                var layer = Resource(snapshot, "Layers", layerName);
                if (layer["IsLocked"].Boolean()) throw new InvalidOperationException("Choose an unlocked destination layer.");
                Set("Layer", layer.Value);
            }
            if (request.Linetype is { } linetype) Set("Linetype", Resource(snapshot, "Linetypes", linetype).Value);
            if (request.Lineweight is { } weight)
            {
                if (!(new[] { "ByLayer", "ByBlock", "Default", "W0", "W5", "W9", "W13", "W15", "W18", "W20", "W25", "W30", "W35", "W40", "W50", "W53", "W60", "W70", "W80", "W90", "W100", "W106", "W120", "W140", "W158", "W200", "W211" }).Contains(weight))
                    throw new ArgumentException("Unknown lineweight.");
                Set("Lineweight", new ShadowValue.Atom("netDxf.Lineweight", weight));
            }
            if (request.Color is { } color && !color.Equals(Color(attribute), StringComparison.OrdinalIgnoreCase))
            {
                var values = new Dictionary<string, ShadowValue>();
                if (color.Length == 7 && color[0] == '#' && int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                { values["R"] = Atom("System.Byte", (rgb >> 16) & 255); values["G"] = Atom("System.Byte", (rgb >> 8) & 255); values["B"] = Atom("System.Byte", rgb & 255); values["UseTrueColor"] = ShadowValue.From(true); }
                else
                {
                    int index = color.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) ? 256 : color.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) ? 0 :
                        int.TryParse(color, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : -1;
                    if (index < 0 || index > 256) throw new ArgumentException("Color requires ByLayer, ByBlock, an ACI index 1–255, or #RRGGBB.");
                    values["Index"] = Atom("System.Int16", index);
                }
                Set("Color", new ShadowValue.Reference(edit.Create("netDxf.AciColor", ShadowObjectKind.Object, values)));
            }
            if (request.Invisible is { } invisible && invisible != ((Flags(attribute) & 1) != 0))
                Set("Flags", Atom("netDxf.Entities.AttributeFlags", invisible ? Flags(attribute) | 1 : Flags(attribute) & ~1));
            if (request.PositionLocked is { } locked)
            {
                Set("IsPositionLocked", ShadowValue.From(locked), false);
                if (packet is not null) packet = packet with { Fields = packet.Fields.SetItem("Locked", ShadowValue.From(locked)) };
            }
            if (packet is not null)
            {
                if (content is not null) packet = packet with { Fields = packet.Fields.SetItem("Content", content) };
                if (!ShadowValue.Equivalent(originalPacket!, packet)) edit.Set(request.Attribute, "TextData", packet);
            }
            if (visual) { Set("ProxyGraphics", ShadowValue.Null); anyVisual = true; }
        }
        if (anyVisual && root["ProxyGraphics"].Exists) edit.Set(insert, "ProxyGraphics", ShadowValue.Null);
        token.ThrowIfCancellationRequested(); edit.PruneUnreachable(); return new("EDIT ATTRIBUTES", edit.Build(), []);
    }

    private static ShadowReader Root(ShadowSnapshot snapshot, ObjectId insert, bool writable)
    {
        var root = DraftingGuard.Entity(snapshot, insert, writable);
        if (root.Type != "Insert" || root["Block"]["IsXRef"].Boolean() || root["Owner"]["IsXRef"].Boolean())
            throw new ArgumentException("Select a local block reference with attributes.");
        return root;
    }
    private static ShadowReader Resource(ShadowSnapshot snapshot, string table, string name) =>
        snapshot.Document[table].Items.FirstOrDefault(r => r["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Exists: true } resource
            ? resource : throw new ArgumentException("Unknown " + table + " resource: " + name);
    private static int Flags(ShadowReader attribute)
    {
        var text = attribute["Flags"].Text;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        var flags = 0;
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            flags |= part switch { "None" => 0, "Hidden" => 1, "Constant" => 2, "Verify" => 4, "Predefined" => 8, _ => throw new NotSupportedException("Unknown attribute flag.") };
        return flags;
    }
    private static string Color(ShadowReader attribute)
    {
        var color = attribute["Color"];
        if (color["UseTrueColor"].Boolean()) return $"#{(int)color["R"].Number():X2}{(int)color["G"].Number():X2}{(int)color["B"].Number():X2}";
        return (int)color["Index"].Number(256) switch { 256 => "ByLayer", 0 => "ByBlock", var index => index.ToString(CultureInfo.InvariantCulture) };
    }
    private static ShadowValue.Atom Atom(string type, double value) => new(type, value.ToString(CultureInfo.InvariantCulture));
}
