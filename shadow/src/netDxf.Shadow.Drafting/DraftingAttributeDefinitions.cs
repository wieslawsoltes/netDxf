using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Drafting;

public sealed record DraftingAttributeDefinition(DraftingAttribute Appearance, bool Verify, bool Preset, Vec3 Position);
public sealed record AttributeDefinitionEdit(ObjectId Definition, string? Tag = null, string? Prompt = null,
    bool? Verify = null, bool? Preset = null, AttributeEdit? Appearance = null);
public sealed record AttributeDefinitionCreation(string Tag, string Prompt = "", string Value = "", Vec3 Position = default,
    double Height = 2.5, string TextStyle = "Standard", string Layer = "0", bool Multiline = false,
    bool Constant = false, bool Verify = false, bool Preset = false, bool Invisible = false, bool PositionLocked = true);
public sealed record AttributeDefinitionChanges(ImmutableArray<AttributeDefinitionEdit> Edits = default,
    ImmutableArray<AttributeDefinitionCreation> Add = default, ImmutableArray<ObjectId> Remove = default,
    ImmutableArray<ObjectId> Order = default);

/// <summary>Reusable ATTDEF/BATTMAN definition plans. Existing instance appearance and values are independent.</summary>
public static class DraftingAttributeDefinitions
{
    private const int Maximum = 10_000;
    public static ImmutableArray<DraftingAttributeDefinition> Read(ShadowSnapshot snapshot, ObjectId block) =>
        Definitions(snapshot, block).Select(d => new DraftingAttributeDefinition(DraftingAttributes.ReadFields(d),
            (DraftingAttributes.Flags(d) & 4) != 0, (DraftingAttributes.Flags(d) & 8) != 0,
            CadTextProjection.Read(d).Text.Origin)).ToImmutableArray();

    public static DraftingPlan Plan(ShadowSnapshot snapshot, ObjectId block, AttributeDefinitionChanges request,
        bool renameInstanceBindings = false, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request); token.ThrowIfCancellationRequested();
        var definitions = Definitions(snapshot, block); var owned = definitions.Select(d => d.Id!.Value).ToHashSet();
        var edits = request.Edits.IsDefault ? [] : request.Edits;
        var additions = request.Add.IsDefault ? [] : request.Add;
        var removals = request.Remove.IsDefault ? [] : request.Remove;
        if (edits.Length + additions.Length + removals.Length > Maximum || definitions.Length + additions.Length > Maximum)
            throw new ArgumentException("Attribute definition edit budget exceeded.");
        var removed = removals.ToHashSet(); var edited = edits.Where(e => e is not null).Select(e => e.Definition).ToHashSet();
        if (edits.Any(e => e is null || !owned.Contains(e.Definition)) || edits.Select(e => e.Definition).Distinct().Count() != edits.Length
            || removals.Any(id => !owned.Contains(id)) || removals.Distinct().Count() != removals.Length
            || edits.Any(e => removed.Contains(e.Definition))) throw new ArgumentException("Choose unique, owned definitions to edit or remove.");
        if (removals.Length > 0 && definitions.Length - removals.Length + additions.Length < 1)
            throw new ArgumentException("A block attribute manager cannot remove the last definition.");
        foreach (var definition in definitions.Where(d => removed.Contains(d.Id!.Value) || edited.Contains(d.Id!.Value)))
            if (definition["Layer"]["IsLocked"].Boolean()) throw new InvalidOperationException("A definition is on a locked layer.");
        var working = snapshot;
        var appearances = edits.Where(e => e.Appearance is not null).Select(e =>
        {
            if (e.Appearance!.Attribute != e.Definition) throw new ArgumentException("Appearance and definition identities must agree.");
            return e.Appearance;
        }).ToImmutableArray();
        if (!appearances.IsEmpty) working = DraftingAttributes.PlanDefinitionFields(working, block, appearances, token).Patch.Apply(working);
        var edit = new ShadowGraphEdit(working); var tags = definitions.ToDictionary(d => d.Id!.Value, d => d["Tag"].Text);
        foreach (var change in edits)
        {
            token.ThrowIfCancellationRequested(); var definition = working.Read(change.Definition);
            if (change.Tag is { } tag) { tags[change.Definition] = ValidateTag(tag); edit.Set(change.Definition, "Tag", S(tags[change.Definition])); }
            if (change.Prompt is { } prompt) { ValidateText(prompt, false); edit.Set(change.Definition, "Prompt", S(prompt)); }
            int flags = DraftingAttributes.Flags(definition);
            if (change.Verify is { } verify) flags = verify ? flags | 4 : flags & ~4;
            if (change.Preset is { } preset) flags = preset ? flags | 8 : flags & ~8;
            if (change.Verify is not null || change.Preset is not null) edit.Set(change.Definition, "Flags", Flags(flags));
        }
        var ids = definitions.Select(d => d.Id!.Value).Where(id => !removed.Contains(id)).ToList();
        if (!request.Order.IsDefaultOrEmpty)
        {
            var retained = ids.ToHashSet();
            if (request.Order.Length != ids.Count || request.Order.Distinct().Count() != ids.Count || request.Order.Any(id => !retained.Contains(id)))
                throw new ArgumentException("Prompt order must contain each retained definition exactly once.");
            var constants = ids.Where(id => (DraftingAttributes.Flags(working.Read(id)) & 2) != 0).ToArray();
            if (!request.Order.Where(constants.Contains).SequenceEqual(constants)) throw new ArgumentException("Constant definitions keep their relative order.");
            ids = request.Order.ToList();
        }
        foreach (var id in removals) edit.Set(id, "Owner", ShadowValue.Null);
        foreach (var creation in additions)
        {
            token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(creation);
            var version = working.Document["DrawingVariables"]["AcadVer"].Text;
            if (creation.Multiline && version != "AutoCad2018" || creation.PositionLocked && version is not ("AutoCad2010" or "AutoCad2013" or "AutoCad2018"))
                throw new NotSupportedException("This drawing's DXF profile cannot store the requested attribute mode. Multiline requires DXF 2018; position locking requires DXF 2010 or later.");
            string tag = ValidateTag(creation.Tag); ValidateText(creation.Prompt, false); ValidateText(creation.Value, creation.Multiline);
            if (!creation.Position.IsFinite || Math.Max(Math.Abs(creation.Position.X), Math.Max(Math.Abs(creation.Position.Y), Math.Abs(creation.Position.Z))) > 1e12
                || !double.IsFinite(creation.Height) || creation.Height < 1e-9 || creation.Height > 1e9) throw new ArgumentException("Choose bounded finite definition coordinates and a positive height.");
            var layer = DraftingAttributes.Resource(working, "Layers", creation.Layer);
            if (layer["IsLocked"].Boolean()) throw new InvalidOperationException("Choose an unlocked definition layer.");
            var style = DraftingAttributes.Resource(working, "TextStyles", creation.TextStyle);
            var color = edit.Create("netDxf.AciColor", ShadowObjectKind.Object, P(("Index", new ShadowValue.Atom("System.Int16", "256"))));
            var transparency = edit.Create("netDxf.Transparency", ShadowObjectKind.Object, P(("Value", new ShadowValue.Atom("System.Int16", "-1"))));
            // The DXF fallback is a single framed string even when visible content
            // has paragraphs. Keep newlines in the embedded packet only.
            var fallback = creation.Value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            var properties = P(("Tag", S(tag)), ("Prompt", S(creation.Prompt)), ("Value", S(fallback)), ("Owner", R(block)),
                ("Position", V(creation.Position)), ("Normal", V(Vec3.UnitZ)), ("Height", N(creation.Height)), ("Width", N(1)),
                ("WidthFactor", N(style["WidthFactor"].Number(1))), ("ObliqueAngle", N(style["ObliqueAngle"].Number())), ("Rotation", N(0)),
                ("Alignment", new ShadowValue.Atom("netDxf.Entities.TextAlignment", "BaselineLeft")), ("IsBackward", ShadowValue.From(false)),
                ("IsUpsideDown", ShadowValue.From(false)), ("Style", style.Value), ("Layer", layer.Value), ("Color", R(color)),
                ("Linetype", DraftingAttributes.Resource(working, "Linetypes", "ByLayer").Value), ("Lineweight", new ShadowValue.Atom("netDxf.Lineweight", "ByLayer")),
                ("Transparency", R(transparency)), ("ColorName", ShadowValue.Null), ("ShadowMode", ShadowValue.Null),
                ("LinetypeScale", N(1)), ("IsVisible", ShadowValue.From(true)), ("ProxyGraphics", ShadowValue.Null),
                ("Flags", Flags((creation.Invisible ? 1 : 0) | (creation.Constant ? 2 : 0) | (creation.Verify ? 4 : 0) | (creation.Preset ? 8 : 0))),
                ("IsPositionLocked", creation.PositionLocked ? ShadowValue.From(true) : ShadowValue.Null), ("HasMText", ShadowValue.From(creation.Multiline)),
                ("TextData", TextData(creation, style.Value)));
            var id = edit.Create("netDxf.Entities.AttributeDefinition", ShadowObjectKind.Object, properties);
            ids.Add(id); tags.Add(id, tag);
        }
        if (ids.Select(id => tags[id]).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ids.Count)
            throw new ArgumentException("Attribute definition tags must be unique, ignoring case.");
        var collection = working.Read(block)["AttributeDefinitions"];
        var pairType = collection.Items.FirstOrDefault().Value is ShadowValue.Structure pair ? pair.TypeName :
            "System.Collections.Generic.KeyValuePair`2[[System.String],[netDxf.Entities.AttributeDefinition]]";
        var pairs = ids.Select(id => (ShadowValue)new ShadowValue.Structure(pairType, P(("Key", S(tags[id])), ("Value", R(id))).ToImmutableDictionary()));
        if (collection.Id is { } collectionId)
        {
            edit.ReplaceSequence(collectionId, "$items", pairs);
            edit.ReplaceSequence(collectionId, "Values", ids.Select(R)); edit.ReplaceSequence(collectionId, "Tags", ids.Select(id => S(tags[id])));
        }
        else edit.Set(block, "AttributeDefinitions", new ShadowValue.Sequence(pairs.ToImmutableArray()));
        if (renameInstanceBindings)
            foreach (var node in working.Objects.Values.Where(n => n.TypeName == "netDxf.Entities.Attribute"))
                if (node["Definition"] is ShadowValue.Reference definition && tags.TryGetValue(definition.Id, out var tag)
                    && !removed.Contains(definition.Id) && node["Tag"] is ShadowValue.Atom old && old.Text != tag)
                    edit.Set(node.Id, "Tag", S(tag));
        token.ThrowIfCancellationRequested(); edit.PruneUnreachable(); working = edit.Build().Apply(working);
        return new("EDIT ATTRIBUTE DEFINITIONS", ShadowGraphPatch.Between(snapshot, working), []);
    }

    private static ShadowReader[] Definitions(ShadowSnapshot snapshot, ObjectId block)
    {
        var root = DraftingBlocks.Definition(snapshot, block);
        var definitions = root["AttributeDefinitions"].Items.Take(Maximum + 1).Select(e => e["Value"]).ToArray();
        if (definitions.Length > Maximum || definitions.Any(d => d.Type != "AttributeDefinition" || d["Owner"].Id != block)
            || definitions.Select(d => d.Id).Distinct().Count() != definitions.Length) throw new ArgumentException("The block has invalid or excessive attribute membership.");
        return definitions;
    }
    private static string ValidateTag(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 255 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '!'))
            throw new ArgumentException("Tags require 1–255 characters without whitespace, control characters or exclamation marks.");
        return value.ToUpperInvariant();
    }
    private static void ValidateText(string value, bool multiline)
    {
        if (value is null || value.Length > 1_000_000 || value.Contains('\0') || !multiline && value.Any(c => c is '\r' or '\n'))
            throw new ArgumentException("Choose bounded text without NUL or single-line paragraph breaks.");
    }
    private static ShadowValue TextData(AttributeDefinitionCreation input, ShadowValue style)
    {
        if (!input.Multiline && !input.PositionLocked) return ShadowValue.Null;
        var fields = P(("Version", ShadowValue.Null), ("Kind", input.Multiline ? new ShadowValue.Atom("System.Int16", "4") : ShadowValue.Null),
            ("SecondaryFlag", ShadowValue.Null), ("FieldLength", ShadowValue.Null), ("Locked", input.PositionLocked ? ShadowValue.From(true) : ShadowValue.Null),
            ("SecondaryAlignment", ShadowValue.Null), ("Auxiliary", new ShadowValue.Sequence([])), ("Content", ShadowValue.Null));
        if (input.Multiline)
        {
            ShadowValue Tag(int code, double number, bool integer = false) => new ShadowValue.Structure("netDxf.IO.DxfTag",
                P(("Code", new ShadowValue.Atom("System.Int16", code.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                ("Value", integer ? new ShadowValue.Atom("System.Int16", number.ToString(System.Globalization.CultureInfo.InvariantCulture)) : N(number))).ToImmutableDictionary());
            fields["Content"] = new ShadowValue.Structure("netDxf.Entities.AttributeMText", P(("Value", S(input.Value.Replace("\r\n", "\\P").Replace("\r", "\\P").Replace("\n", "\\P"))),
                ("Style", style), ("Fields", new ShadowValue.Sequence([Tag(10, input.Position.X), Tag(20, input.Position.Y), Tag(30, input.Position.Z), Tag(40, input.Height), Tag(41, 0), Tag(71, 1, true)]))).ToImmutableDictionary());
        }
        return new ShadowValue.Structure("netDxf.Entities.AttributeTextData", fields.ToImmutableDictionary());
    }
    private static Dictionary<string, ShadowValue> P(params (string Name, ShadowValue Value)[] fields) => fields.ToDictionary(p => p.Name, p => p.Value);
    private static ShadowValue S(string value) => ShadowValue.From(value);
    private static ShadowValue N(double value) => ShadowValue.From(value);
    private static ShadowValue R(ObjectId id) => new ShadowValue.Reference(id);
    private static ShadowValue Flags(int value) => new ShadowValue.Atom("netDxf.Entities.AttributeFlags", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    private static ShadowValue V(Vec3 value) => new ShadowValue.Structure("netDxf.Vector3", P(("X", N(value.X)), ("Y", N(value.Y)), ("Z", N(value.Z))).ToImmutableDictionary());
}
