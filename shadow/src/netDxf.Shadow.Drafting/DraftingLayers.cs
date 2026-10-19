using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Drafting;

public sealed record DraftingLayer(ObjectId Id, string Name, string Color, bool Visible, bool Frozen, bool Locked,
    bool Plot, string Linetype, string Lineweight, string Description, int Transparency, bool FrozenInNewViewports, int ColorIndex, bool TrueColor);
public sealed record LayerEdit(ObjectId? Id, string? Name = null, string? Color = null, bool? Visible = null,
    bool? Frozen = null, bool? Locked = null, bool? Plot = null, string? Linetype = null, string? Lineweight = null,
    string? Description = null, int? Transparency = null, bool? FrozenInNewViewports = null, int? ColorIndex = null);

/// <summary>Layer edits remain ordinary immutable graph transactions and are reusable by any host.</summary>
public static class DraftingLayers
{
    public static ImmutableArray<DraftingLayer> Read(ShadowSnapshot snapshot) => snapshot.Document["Layers"].Items
        .Select(l => new DraftingLayer(l.Id!.Value, l["Name"].Text,
            $"#{(int)l["Color"]["R"].Number():X2}{(int)l["Color"]["G"].Number():X2}{(int)l["Color"]["B"].Number():X2}",
            l["IsVisible"].Boolean(true), l["IsFrozen"].Boolean(), l["IsLocked"].Boolean(), l["Plot"].Boolean(true),
            l["Linetype"]["Name"].Text, l["Lineweight"].Text, l["Description"].Text, (int)l["Transparency"]["Value"].Number(),
            l["IsFrozenInNewViewports"].Boolean(), (int)l["Color"]["Index"].Number(), l["Color"]["UseTrueColor"].Boolean()))
        .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();

    public static DraftingPlan Plan(ShadowSnapshot snapshot, LayerEdit request)
    {
        var layers = snapshot.Document["Layers"].Items.ToArray(); var edit = new ShadowGraphEdit(snapshot);
        var creating = request.Id is null; var id = request.Id ?? new ObjectId("N:" + Guid.NewGuid().ToString("N"));
        var current = layers.FirstOrDefault(l => l.Id == id);
        if (!creating && !current.Exists) throw new ArgumentException("Unknown layer.");
        if (request.Name is { } name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(c => char.IsControl(c) || "<>/\\\":;?*|,=`".Contains(c)))
                throw new ArgumentException("Enter a valid layer name (1–255 printable characters).");
            if (layers.Any(l => l.Id != id && l["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Layer names must be unique, ignoring case.");
            if (!creating && current["Name"].Text == "0" && name != "0") throw new ArgumentException("Layer 0 cannot be renamed.");
        }
        else if (creating) throw new ArgumentException("New layers require a name.");
        if (creating)
        {
            var continuous = snapshot.Document["Linetypes"].Items.FirstOrDefault(l => l["Name"].Text == "Continuous");
            edit.Create("netDxf.Tables.Layer", ShadowObjectKind.Table, new Dictionary<string, ShadowValue>
            {
                ["Name"] = ShadowValue.From(request.Name!), ["IsVisible"] = ShadowValue.From(true), ["IsLocked"] = ShadowValue.From(false),
                ["IsFrozen"] = ShadowValue.From(false), ["Plot"] = ShadowValue.From(true), ["Linetype"] = continuous.Value,
                ["Lineweight"] = new ShadowValue.Atom("netDxf.Lineweight", "Default")
            }, id);
            edit.ReplaceSequence(snapshot.Root, "Layers", layers.Select(l => l.Value).Append(new ShadowValue.Reference(id)));
        }
        if (request.Name is not null)
        {
            edit.Set(id, "Name", ShadowValue.From(request.Name));
            if (!creating && current["Name"].Text.Equals(DraftingResources.Current(snapshot, "layer"), StringComparison.OrdinalIgnoreCase))
                edit.Set(snapshot.Document["DrawingVariables"].Id!.Value, "CLayer", ShadowValue.From(request.Name));
        }
        if (!creating && current["Name"].Text.Equals(DraftingResources.Current(snapshot, "layer"), StringComparison.OrdinalIgnoreCase) && request.Frozen == true)
            throw new ArgumentException("Choose another current layer before freezing this layer.");
        if (request.Description is { } description)
        { if (description.Length > 1024) throw new ArgumentException("Layer descriptions are limited to 1024 characters."); edit.Set(id, "Description", ShadowValue.From(description)); }
        if (request.Transparency is { } transparency)
        {
            if (transparency < 0 || transparency > 90) throw new ArgumentException("Layer transparency must be from 0 to 90 percent.");
            var alpha = edit.Create("netDxf.Transparency", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
                { ["Value"] = new ShadowValue.Atom("System.Int16", transparency.ToString(CultureInfo.InvariantCulture)) });
            edit.Set(id, "Transparency", new ShadowValue.Reference(alpha));
        }
        if (request.ColorIndex is { } index)
        {
            if (request.Color is not null || index < 1 || index > 255) throw new ArgumentException("Choose either RGB or an indexed layer color from 1 to 255.");
            var colorId = edit.Create("netDxf.AciColor", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
                { ["Index"] = new ShadowValue.Atom("System.Int16", index.ToString(CultureInfo.InvariantCulture)) });
            edit.Set(id, "Color", new ShadowValue.Reference(colorId));
        }
        foreach (var (property, value) in new[] { ("IsVisible", request.Visible), ("IsFrozen", request.Frozen), ("IsFrozenInNewViewports", request.FrozenInNewViewports), ("IsLocked", request.Locked), ("Plot", request.Plot) })
            if (value is { } flag) edit.Set(id, property, ShadowValue.From(flag));
        if (request.Color is { } color)
        {
            if (color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                throw new ArgumentException("Colors use #RRGGBB.");
            var colorId = edit.Create("netDxf.AciColor", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
            {
                ["R"] = new ShadowValue.Atom("System.Byte", ((rgb >> 16) & 255).ToString(CultureInfo.InvariantCulture)),
                ["G"] = new ShadowValue.Atom("System.Byte", ((rgb >> 8) & 255).ToString(CultureInfo.InvariantCulture)),
                ["B"] = new ShadowValue.Atom("System.Byte", (rgb & 255).ToString(CultureInfo.InvariantCulture)),
                ["UseTrueColor"] = ShadowValue.From(true)
            });
            edit.Set(id, "Color", new ShadowValue.Reference(colorId));
        }
        if (request.Linetype is { } line)
        {
            var linetype = snapshot.Document["Linetypes"].Items.FirstOrDefault(l => l["Name"].Text.Equals(line, StringComparison.OrdinalIgnoreCase));
            if (!linetype.Exists || line.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || line.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose an explicit existing linetype for the layer.");
            edit.Set(id, "Linetype", linetype.Value);
        }
        if (request.Lineweight is { } weight)
        {
            if (weight is not ("Default" or "W0" or "W5" or "W9" or "W13" or "W15" or "W18" or "W20" or "W25" or "W30" or "W35" or "W40" or "W50" or "W53" or "W60" or "W70" or "W80" or "W90" or "W100" or "W106" or "W120" or "W140" or "W158" or "W200" or "W211"))
                throw new ArgumentException("Unknown layer lineweight.");
            edit.Set(id, "Lineweight", new ShadowValue.Atom("netDxf.Lineweight", weight));
        }
        edit.PruneUnreachable(); return new(creating ? "NEW LAYER" : "LAYER", edit.Build(), []);
    }

    public static DraftingPlan Assign(ShadowSnapshot snapshot, IEnumerable<ObjectId> entities, ObjectId layer)
    {
        var destination = snapshot.Document["Layers"].Items.FirstOrDefault(l => l.Id == layer);
        if (!destination.Exists || destination["IsLocked"].Boolean()) throw new ArgumentException("Choose an unlocked layer.");
        var edit = new ShadowGraphEdit(snapshot); var count = 0;
        foreach (var id in entities.Distinct())
        { DraftingGuard.Entity(snapshot, id); if (++count > 10_000) throw new ArgumentException("Layer assignment budget exceeded."); edit.Set(id, "Layer", destination.Value); }
        return new("CHANGE LAYER", edit.Build(), []);
    }
}
