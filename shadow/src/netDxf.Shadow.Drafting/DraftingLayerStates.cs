using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Drafting;

[Flags]
public enum LayerStateFields
{
    None = 0, Hidden = 1, Frozen = 2, Locked = 4, Plot = 8, NewVpFrozen = 16,
    Color = 64, Linetype = 128, Lineweight = 256, Transparency = 512,
    All = Hidden | Frozen | Locked | Plot | NewVpFrozen | Color | Linetype | Lineweight | Transparency
}

public sealed record LayerStateEdit(string Action, ObjectId? Id = null, string? Name = null,
    string Description = "", LayerStateFields Fields = LayerStateFields.All,
    bool RestoreCurrentLayer = true, bool TurnOffUnlistedLayers = false,
    ImmutableArray<LayerStateEntryEdit> Entries = default, ImmutableArray<string> AddLayers = default,
    ImmutableArray<string> RemoveLayers = default, string? CurrentLayer = null);
public sealed record DraftingLayerState(ObjectId Id, string Name, string Description, string CurrentLayer,
    bool PaperSpace, ImmutableArray<string> Layers, LayerStateFields Fields = LayerStateFields.All, int RestoreMask = 2047);

/// <summary>Native DXF layer-state authoring and selective restoration as detached, atomic plans.</summary>
public static partial class DraftingLayerStates
{
    [Flags]
    private enum StoredFlags { None = 0, Hidden = 1, Frozen = 2, Locked = 4, Plot = 8, NewVpFrozen = 16, VpFrozen = 32 }
    private static int Flags(ShadowReader property) => Enum.TryParse<StoredFlags>(property["Flags"].Text, out var flags)
        ? (int)flags : throw new ArgumentException("Invalid saved layer flags.");
    private static ShadowReader Manager(ShadowSnapshot snapshot) => snapshot.Document["Layers"]["StateManager"];
    public static ImmutableArray<DraftingLayerState> Read(ShadowSnapshot snapshot) => Manager(snapshot).Items
        .Select(s => new DraftingLayerState(s.Id!.Value, s["Name"].Text, s["Description"].Text, s["CurrentLayer"].Text,
            s["PaperSpace"].Boolean(), s["Properties"].Items.Select(p => p["Key"].Text).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            FieldsFromMask(Mask(s)), Mask(s)))
        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();

    public static DraftingPlan Plan(ShadowSnapshot snapshot, LayerStateEdit request, string layout = "Model",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if ((request.Fields & ~LayerStateFields.All) != 0) throw new ArgumentException("Unsupported layer-state restore fields.");
        var manager = Manager(snapshot);
        if (manager.Id is not { } managerId) throw new NotSupportedException("The source has no layer-state manager.");
        var states = manager.Items.ToArray();
        if (states.Length > 1000) throw new ArgumentException("Layer-state manager exceeds the 1000-state editing budget.");
        var current = states.FirstOrDefault(s => s.Id == request.Id);
        if (request.Id is not null && !current.Exists) throw new ArgumentException("Unknown layer state.");
        var edit = new ShadowGraphEdit(snapshot);
        switch (request.Action)
        {
            case "save":
                DraftingResources.ValidateName(request.Name, states, request.Id);
                if (request.Description is null || request.Description.Length > 4096) throw new ArgumentException("Layer-state descriptions are limited to 4096 characters.");
                if (!current.Exists && states.Length == 1000) throw new ArgumentException("The layer-state budget is exhausted.");
                var layers = snapshot.Document["Layers"].Items.ToArray();
                if (layers.Length > 10000) throw new ArgumentException("Layer states are limited to 10000 layers.");
                var id = request.Id ?? edit.Create("netDxf.Objects.LayerState", ShadowObjectKind.Table,
                    new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(request.Name!), ["Properties"] = new ShadowValue.Sequence([]) });
                if (!current.Exists) edit.ReplaceSequence(managerId, "$items", states.Select(s => s.Value).Append(new ShadowValue.Reference(id)));
                edit.Set(id, "Name", ShadowValue.From(request.Name!));
                edit.Set(id, "Description", ShadowValue.From(request.Description));
                edit.Set(id, "CurrentLayer", ShadowValue.From(DraftingResources.Current(snapshot, "layer")));
                edit.Set(id, "PaperSpace", ShadowValue.From(!layout.Equals("Model", StringComparison.OrdinalIgnoreCase)));
                if (!current.Exists) SetMask(edit, id, MaskFromFields(request.Fields));
                var entries = new List<ShadowValue>(layers.Length);
                var previousProperties = current["Properties"].Items.ToDictionary(p => p["Key"].Text, p => p["Value"], StringComparer.OrdinalIgnoreCase);
                foreach (var layer in layers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    previousProperties.TryGetValue(layer["Name"].Text, out var previous);
                    var flags = (!layer["IsVisible"].Boolean(true) ? 1 : 0) | (layer["IsFrozen"].Boolean() ? 2 : 0)
                        | (layer["IsLocked"].Boolean() ? 4 : 0) | (layer["Plot"].Boolean(true) ? 8 : 0) | (layer["IsFrozenInNewViewports"].Boolean() ? 16 : 0);
                    if (previous.Exists) flags |= Flags(previous) & ~31;
                    var property = edit.Create("netDxf.Objects.LayerStateProperties", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
                    {
                        ["Name"] = layer["Name"].Value, ["Flags"] = new ShadowValue.Atom("netDxf.Objects.LayerPropertiesFlags", flags.ToString(CultureInfo.InvariantCulture)),
                        ["Color"] = CopyValue(edit, layer["Color"]), ["Transparency"] = CopyValue(edit, layer["Transparency"]),
                        ["LinetypeName"] = layer["Linetype"]["Name"].Value, ["Lineweight"] = layer["Lineweight"].Value
                    });
                    entries.Add(new ShadowValue.Structure("System.Collections.Generic.KeyValuePair`2", new Dictionary<string, ShadowValue>
                        { ["Key"] = layer["Name"].Value, ["Value"] = new ShadowValue.Reference(property) }.ToImmutableDictionary()));
                }
                edit.ReplaceSequence(id, "Properties", entries);
                break;
            case "rename":
                RequireState(current); DraftingResources.ValidateName(request.Name, states, request.Id);
                edit.Set(current.Id!.Value, "Name", ShadowValue.From(request.Name!)); break;
            case "delete":
                RequireState(current);
                edit.ReplaceSequence(managerId, "$items", states.Where(s => s.Id != current.Id).Select(s => s.Value)); break;
            case "restore":
                RequireState(current); Restore(snapshot, edit, current, request, cancellationToken); break;
            case "options":
                RequireState(current);
                SetMask(edit, current.Id!.Value, (Mask(current) & ~SupportedMask) | MaskFromFields(request.Fields));
                break;
            case "edit":
                RequireState(current); EditEntries(snapshot, edit, current, request, cancellationToken); break;
            default: throw new ArgumentException("Use save, rename, delete, restore, options or edit for a layer state.");
        }
        cancellationToken.ThrowIfCancellationRequested(); edit.PruneUnreachable();
        return new(request.Action.ToUpperInvariant() + " LAYER STATE", edit.Build(), []);
    }

    private static void Restore(ShadowSnapshot snapshot, ShadowGraphEdit edit, ShadowReader state, LayerStateEdit request, CancellationToken token)
    {
        var layers = snapshot.Document["Layers"].Items.ToDictionary(l => l["Name"].Text, StringComparer.OrdinalIgnoreCase);
        var linetypes = snapshot.Document["Linetypes"].Items.ToDictionary(l => l["Name"].Text, StringComparer.OrdinalIgnoreCase);
        var entries = state["Properties"].Items.ToArray();
        if (entries.Length > 10000) throw new ArgumentException("Layer-state restoration is limited to 10000 layers.");
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentName = request.RestoreCurrentLayer ? state["CurrentLayer"].Text : DraftingResources.Current(snapshot, "layer");
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var p = entry["Value"]; var name = p["Name"].Text;
            if (!listed.Add(name) || !name.Equals(entry["Key"].Text, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Layer state has duplicate or inconsistent layer names.");
            var flags = (LayerStateFields)Flags(p);
            // Preserve viewport/unknown stored bits; only explicitly supported fields affect global layers.
            if (name.Equals(currentName, StringComparison.OrdinalIgnoreCase) && request.Fields.HasFlag(LayerStateFields.Frozen) && flags.HasFlag(LayerStateFields.Frozen))
                throw new ArgumentException("The restored current layer would be frozen. Choose another current layer or omit frozen state restoration.");
            // The native manager retains name-based references to saved layers. It
            // refuses removing a recreated layer, so that creation would have no
            // valid inverse for undo. Require explicit resource repair first.
            if (!layers.TryGetValue(name, out var layer))
                throw new ArgumentException("The saved layer '" + name + "' is missing. Repair the drawing's layer resources before restoring this state.");
            var id = layer.Id!.Value;
            foreach (var (field, property, invert) in new[] { (LayerStateFields.Hidden, "IsVisible", true), (LayerStateFields.Frozen, "IsFrozen", false),
                (LayerStateFields.Locked, "IsLocked", false), (LayerStateFields.Plot, "Plot", false), (LayerStateFields.NewVpFrozen, "IsFrozenInNewViewports", false) })
                if (request.Fields.HasFlag(field)) edit.Set(id, property, ShadowValue.From(flags.HasFlag(field) != invert));
            if (request.Fields.HasFlag(LayerStateFields.Color)) edit.Set(id, "Color", CopyValue(edit, p["Color"]));
            if (request.Fields.HasFlag(LayerStateFields.Transparency)) edit.Set(id, "Transparency", CopyValue(edit, p["Transparency"]));
            if (request.Fields.HasFlag(LayerStateFields.Lineweight)) edit.Set(id, "Lineweight", p["Lineweight"].Value);
            if (request.Fields.HasFlag(LayerStateFields.Linetype))
            {
                if (!linetypes.TryGetValue(p["LinetypeName"].Text, out var line) || line["Name"].Text is "ByLayer" or "ByBlock")
                    throw new ArgumentException("Load the saved linetype '" + p["LinetypeName"].Text + "' before restoring this state, or omit linetype restoration.");
                edit.Set(id, "Linetype", line.Value);
            }
        }
        if (request.RestoreCurrentLayer)
        {
            if (!layers.ContainsKey(currentName) && !listed.Contains(currentName)) throw new ArgumentException("The saved current layer is unavailable.");
            if ((!listed.Contains(currentName) || !request.Fields.HasFlag(LayerStateFields.Frozen)) && layers.TryGetValue(currentName, out var current) && current["IsFrozen"].Boolean())
                throw new ArgumentException("Thaw the saved current layer before making it current.");
            edit.Set(snapshot.Document["DrawingVariables"].Id!.Value, "CLayer", ShadowValue.From(currentName));
        }
        if (request.TurnOffUnlistedLayers)
            foreach (var layer in layers.Values.Where(l => !listed.Contains(l["Name"].Text))) edit.Set(layer.Id!.Value, "IsVisible", ShadowValue.From(false));
    }

    private static ShadowValue CopyValue(ShadowGraphEdit edit, ShadowReader value)
    {
        if (value.Object is not { } node) throw new ArgumentException("The saved layer resource is unavailable.");
        // Packed alpha zero means opaque in a saved state and ByBlock on a live layer.
        // Cross that boundary by effective value, matching native CopyFrom/CopyTo.
        // Imports/exports within saved-state semantics keep the exact packet separately.
        var properties = node.TypeName == "netDxf.Transparency" && value["StoredAlphaValue"].Value is ShadowValue.Atom { Text: "0" }
            ? node.Properties.Remove("StoredAlphaValue") : node.Properties;
        return new ShadowValue.Reference(edit.Create(node.TypeName, node.Kind, properties));
    }
    private static void RequireState(ShadowReader state) { if (!state.Exists) throw new ArgumentException("Choose an existing layer state."); }
}
