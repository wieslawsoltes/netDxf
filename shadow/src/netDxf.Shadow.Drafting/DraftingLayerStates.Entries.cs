using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Drafting;

public sealed record LayerStateEntryEdit(string Name, bool? Visible = null, bool? Frozen = null, bool? Locked = null,
    bool? Plot = null, bool? FrozenInNewViewports = null, string? Color = null, int? ColorIndex = null,
    string? Linetype = null, string? Lineweight = null, int? Transparency = null);
public sealed record SavedLayerProperties(string Name, int Flags, string Color, int ColorIndex, bool TrueColor,
    string Linetype, string Lineweight, int Transparency, int? StoredAlphaValue);
public sealed record DraftingLayerStateDetails(DraftingLayerState State, ImmutableArray<SavedLayerProperties> Entries);

public static partial class DraftingLayerStates
{
    private const int SupportedMask = 1 | 2 | 4 | 8 | 16 | 32 | 64 | 128 | 1024;
    private static readonly (LayerStateFields Field, int Mask)[] MaskFields =
    [
        (LayerStateFields.Hidden, 1), (LayerStateFields.Frozen, 2), (LayerStateFields.Locked, 4),
        (LayerStateFields.Plot, 8), (LayerStateFields.NewVpFrozen, 16), (LayerStateFields.Color, 32),
        (LayerStateFields.Linetype, 64), (LayerStateFields.Lineweight, 128), (LayerStateFields.Transparency, 1024)
    ];
    /// <summary>Converts native group 91 mask bits into supported global restore fields; unknown bits are not interpreted.</summary>
    public static LayerStateFields FieldsFromMask(int mask) => MaskFields.Aggregate(LayerStateFields.None,
        (result, pair) => result | ((mask & pair.Mask) != 0 ? pair.Field : LayerStateFields.None));
    /// <summary>Converts supported restore fields to native group 91 mask bits.</summary>
    public static int MaskFromFields(LayerStateFields fields)
    {
        if ((fields & ~LayerStateFields.All) != 0) throw new ArgumentException("Unsupported layer-state fields.");
        return MaskFields.Aggregate(0, (result, pair) => result | (fields.HasFlag(pair.Field) ? pair.Mask : 0));
    }
    private static int Mask(ShadowReader state) => state["RestoreMask"].Exists
        ? int.TryParse(state["RestoreMask"].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value
            : ParseMaskNames(state["RestoreMask"].Text) : 2047;
    private static int ParseMaskNames(string text)
    {
        var names = new Dictionary<string, int>(StringComparer.Ordinal)
        { ["None"]=0,["On"]=1,["Frozen"]=2,["Locked"]=4,["Plot"]=8,["NewViewport"]=16,["Color"]=32,
            ["Linetype"]=64,["Lineweight"]=128,["PlotStyle"]=256,["CurrentViewport"]=512,["Transparency"]=1024,
            ["All"]=2047,["HiddenState"]=32768,["LastRestored"]=65536 };
        return text.Split(',').Aggregate(0, (mask, name) => mask | (names.TryGetValue(name.Trim(), out var bit)
            ? bit : throw new ArgumentException("Invalid layer-state restore mask.")));
    }
    private static void SetMask(ShadowGraphEdit edit, ObjectId id, int mask) =>
        edit.Set(id, "RestoreMask", new ShadowValue.Atom("netDxf.Objects.LayerStateRestoreMask", mask.ToString(CultureInfo.InvariantCulture)));
    public static DraftingLayerStateDetails Details(ShadowSnapshot snapshot, ObjectId id)
    {
        var summary = Read(snapshot).FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException("Unknown layer state.");
        var entries = snapshot.Read(id)["Properties"].Items.Take(10001).ToArray();
        if (entries.Length > 10000) throw new ArgumentException("Layer states are limited to 10000 layers.");
        return new(summary, entries.Select(e => ReadEntry(e["Value"])).ToImmutableArray());
    }
    private static SavedLayerProperties ReadEntry(ShadowReader p) => new(p["Name"].Text, Flags(p),
        $"#{(int)p["Color"]["R"].Number():X2}{(int)p["Color"]["G"].Number():X2}{(int)p["Color"]["B"].Number():X2}",
        (int)p["Color"]["Index"].Number(), p["Color"]["UseTrueColor"].Boolean(), p["LinetypeName"].Text,
        p["Lineweight"].Text, (int)p["Transparency"]["Value"].Number(),
        p["Transparency"]["StoredAlphaValue"].Value is ShadowValue.Atom ? (int)p["Transparency"]["StoredAlphaValue"].Number() : null);

    private static void EditEntries(ShadowSnapshot snapshot, ShadowGraphEdit edit, ShadowReader state, LayerStateEdit request, CancellationToken token)
    {
        var entries = state["Properties"].Items.ToDictionary(p => p["Key"].Text, p => p["Value"], StringComparer.OrdinalIgnoreCase);
        var layers = snapshot.Document["Layers"].Items.ToDictionary(l => l["Name"].Text, StringComparer.OrdinalIgnoreCase);
        var changes = request.Entries.IsDefault ? [] : request.Entries;
        var add = request.AddLayers.IsDefault ? [] : request.AddLayers;
        var remove = request.RemoveLayers.IsDefault ? [] : request.RemoveLayers;
        if (entries.Count > 10000 || changes.Length + add.Length + remove.Length > 10000) throw new ArgumentException("Saved-entry editing is limited to 10000 entries.");
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in remove)
        {
            token.ThrowIfCancellationRequested();
            if (name is null || !touched.Add(name) || !entries.Remove(name)) throw new ArgumentException("Choose unique existing saved layers to remove.");
        }
        var values = entries.ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var name in add)
        {
            token.ThrowIfCancellationRequested();
            if (name is null || !touched.Add(name) || values.ContainsKey(name) || !layers.TryGetValue(name, out var layer))
                throw new ArgumentException("Add unique drawing layers which are not already saved.");
            var flags = (!layer["IsVisible"].Boolean(true) ? 1 : 0) | (layer["IsFrozen"].Boolean() ? 2 : 0)
                | (layer["IsLocked"].Boolean() ? 4 : 0) | (layer["Plot"].Boolean(true) ? 8 : 0) | (layer["IsFrozenInNewViewports"].Boolean() ? 16 : 0);
            var property = edit.Create("netDxf.Objects.LayerStateProperties", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
            {
                ["Name"] = layer["Name"].Value, ["Flags"] = new ShadowValue.Atom("netDxf.Objects.LayerPropertiesFlags", flags.ToString(CultureInfo.InvariantCulture)),
                ["Color"] = CopyValue(edit, layer["Color"]), ["Transparency"] = CopyValue(edit, layer["Transparency"]),
                ["LinetypeName"] = layer["Linetype"]["Name"].Value, ["Lineweight"] = layer["Lineweight"].Value
            });
            values.Add(layer["Name"].Text, new ShadowValue.Reference(property));
        }
        foreach (var change in changes)
        {
            token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(change);
            if (change.Name is null || !touched.Add(change.Name) || !entries.TryGetValue(change.Name, out var property))
                throw new ArgumentException("Edit unique existing saved layers; separate additions and edits.");
            var id = property.Id!.Value; var flags = Flags(property);
            foreach (var (flag, value) in new[] { (1, change.Visible is { } visible ? !visible : (bool?)null), (2, change.Frozen),
                (4, change.Locked), (8, change.Plot), (16, change.FrozenInNewViewports) })
                if (value is { } set) flags = set ? flags | flag : flags & ~flag;
            edit.Set(id, "Flags", new ShadowValue.Atom("netDxf.Objects.LayerPropertiesFlags", flags.ToString(CultureInfo.InvariantCulture)));
            ApplySavedAppearance(snapshot, edit, id, change);
        }
        if (request.CurrentLayer is { } current)
        {
            if (!layers.TryGetValue(current, out var layer)) throw new ArgumentException("Choose an existing drawing layer as the saved current layer.");
            edit.Set(state.Id!.Value, "CurrentLayer", layer["Name"].Value);
        }
        if (values.Count > 10000) throw new ArgumentException("Layer states are limited to 10000 layers.");
        edit.ReplaceSequence(state.Id!.Value, "Properties", values.Select(pair => Pair(pair.Key, pair.Value)));
    }
    private static ShadowValue Pair(string name, ShadowValue value) => new ShadowValue.Structure("System.Collections.Generic.KeyValuePair`2",
        ImmutableDictionary<string, ShadowValue>.Empty.Add("Key", ShadowValue.From(name)).Add("Value", value));
    private static void ApplySavedAppearance(ShadowSnapshot snapshot, ShadowGraphEdit edit, ObjectId id, LayerStateEntryEdit change)
    {
        if (change.Color is not null && change.ColorIndex is not null) throw new ArgumentException("Choose RGB or indexed color, not both.");
        if (change.ColorIndex is { } index)
        {
            if (index is < 1 or > 255) throw new ArgumentException("Indexed layer colors range from 1 to 255.");
            edit.Set(id, "Color", new ShadowValue.Reference(edit.Create("netDxf.AciColor", ShadowObjectKind.Object,
                new Dictionary<string, ShadowValue> { ["Index"] = new ShadowValue.Atom("System.Int16", index.ToString(CultureInfo.InvariantCulture)) })));
        }
        if (change.Color is { } color)
        {
            if (color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                throw new ArgumentException("Colors use #RRGGBB.");
            edit.Set(id, "Color", ColorValue(edit, rgb, null));
        }
        if (change.Transparency is { } transparency)
        {
            if (transparency is < 0 or > 90) throw new ArgumentException("Transparency ranges from 0 to 90 percent.");
            edit.Set(id, "Transparency", AlphaValue(edit, transparency, null));
        }
        if (change.Lineweight is { } weight)
        {
            if (weight is not ("Default" or "W0" or "W5" or "W9" or "W13" or "W15" or "W18" or "W20" or "W25" or "W30" or "W35" or "W40" or "W50" or "W53" or "W60" or "W70" or "W80" or "W90" or "W100" or "W106" or "W120" or "W140" or "W158" or "W200" or "W211"))
                throw new ArgumentException("Unknown layer lineweight.");
            edit.Set(id, "Lineweight", new ShadowValue.Atom("netDxf.Lineweight", weight));
        }
        if (change.Linetype is { } line)
        {
            var resource = snapshot.Document["Linetypes"].Items.FirstOrDefault(l => l["Name"].Text.Equals(line, StringComparison.OrdinalIgnoreCase));
            if (!resource.Exists || line.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || line.Equals("ByBlock", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an explicit existing drawing linetype.");
            edit.Set(id, "LinetypeName", resource["Name"].Value);
        }
    }
    private static ShadowValue ColorValue(ShadowGraphEdit edit, int? rgb, int? index)
    {
        var properties = new Dictionary<string, ShadowValue>();
        if (rgb is { } number)
        {
            properties["R"] = new ShadowValue.Atom("System.Byte", ((number >> 16) & 255).ToString(CultureInfo.InvariantCulture));
            properties["G"] = new ShadowValue.Atom("System.Byte", ((number >> 8) & 255).ToString(CultureInfo.InvariantCulture));
            properties["B"] = new ShadowValue.Atom("System.Byte", (number & 255).ToString(CultureInfo.InvariantCulture));
            properties["UseTrueColor"] = ShadowValue.From(true);
        }
        else if (index is { } indexed) properties["Index"] = new ShadowValue.Atom("System.Int16", indexed.ToString(CultureInfo.InvariantCulture));
        return new ShadowValue.Reference(edit.Create("netDxf.AciColor", ShadowObjectKind.Object, properties));
    }
    private static ShadowValue AlphaValue(ShadowGraphEdit edit, int value, int? stored)
    {
        var properties = new Dictionary<string, ShadowValue> { ["Value"] = new ShadowValue.Atom("System.Int16", value.ToString(CultureInfo.InvariantCulture)) };
        if (stored is { } alpha) properties["StoredAlphaValue"] = new ShadowValue.Atom("System.Int32", alpha.ToString(CultureInfo.InvariantCulture));
        return new ShadowValue.Reference(edit.Create("netDxf.Transparency", ShadowObjectKind.Object, properties));
    }
}
