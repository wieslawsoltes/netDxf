using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Drafting;

public sealed record LayerIsolationEdit(string Action, ImmutableArray<ObjectId> Layers = default, string Mode = "off");
public sealed record LayerIsolationStatus(bool Active, string? Mode, int LayerCount, string Notice, bool Valid = true);

/// <summary>Global layer isolation with native, undoable XDATA restore receipts.
/// Receipts follow layer identity through renaming, branches and DXF transport.</summary>
public static class DraftingLayerIsolation
{
    public const string ApplicationName = "SHADOW_LAYER_ISOLATION";
    private const int MaximumLayers = 10000;
    private static readonly (int Bit, string Property)[] Properties = [(1, "IsVisible"), (2, "IsFrozen"), (4, "IsLocked")];
    private sealed record Receipt(string Mode, int Before, int Applied);

    public static LayerIsolationStatus Read(ShadowSnapshot snapshot)
    {
        try
        {
            var receipts = Layers(snapshot).Select(ReadReceipt).Where(r => r is not null).ToArray();
            var modes = receipts.Select(r => r!.Mode).Distinct(StringComparer.Ordinal).ToArray();
            return new(receipts.Length > 0, modes.Length == 1 ? modes[0] : receipts.Length == 0 ? null : "mixed", receipts.Length,
                "Global layer flags only. Restore keeps unrelated properties, new layers and flags changed after isolation. No locked-layer fading or viewport-specific freeze.");
        }
        catch (ArgumentException error)
        {
            // A foreign/malformed receipt blocks isolation, not all other resource
            // editing. Never infer flags or delete unrecognized metadata.
            return new(false, null, 0, error.Message, false);
        }
    }

    public static DraftingPlan Plan(ShadowSnapshot snapshot, LayerIsolationEdit request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(request);
        token.ThrowIfCancellationRequested();
        if (request.Action is not ("isolate" or "restore")) throw new ArgumentException("Use isolate or restore.");
        if (request.Action == "restore")
        {
            if (!request.Layers.IsDefaultOrEmpty) throw new ArgumentException("Restore applies the previous isolation; do not supply layers.");
            return Restore(snapshot, token);
        }
        if (request.Mode is not ("off" or "lock")) throw new ArgumentException("Layer isolation supports off or lock.");
        if (request.Layers.IsDefaultOrEmpty || request.Layers.Length > MaximumLayers)
            throw new ArgumentException("Choose 1–10000 layers to isolate.");
        var requested = request.Layers.ToHashSet();
        var originalIds = Layers(snapshot).Select(l => l.Id!.Value).ToHashSet();
        if (!requested.IsSubsetOf(originalIds)) throw new ArgumentException("An isolation layer is missing.");

        // Replace a prior isolation from its restored baseline without publishing
        // or recording the intermediate state.
        var restored = Restore(snapshot, token).Patch.Apply(snapshot);
        var changes = new List<(ShadowReader Layer, int Before, int Applied)>();
        foreach (var layer in Layers(restored))
        {
            token.ThrowIfCancellationRequested();
            var before = Flags(layer);
            var applied = requested.Contains(layer.Id!.Value) ? 1 :
                request.Mode == "off" ? before & ~1 : before | 4;
            if (before != applied) changes.Add((layer, before, applied));
        }
        var edit = new ShadowGraphEdit(restored);
        if (changes.Count > 0)
        {
            var registry = Registry(restored, edit);
            foreach (var (layer, before, applied) in changes)
            {
                token.ThrowIfCancellationRequested();
                foreach (var (bit, property) in Properties)
                    if ((before & bit) != (applied & bit)) edit.Set(layer.Id!.Value, property, ShadowValue.From((applied & bit) != 0));
                WriteReceipt(edit, layer, registry.Id, registry.Name, new(request.Mode, before, applied));
            }
        }
        edit.PruneUnreachable(); token.ThrowIfCancellationRequested();
        var target = edit.Build().Apply(restored);
        return new("ISOLATE LAYERS", ShadowGraphPatch.Between(snapshot, target), []);
    }

    private static DraftingPlan Restore(ShadowSnapshot snapshot, CancellationToken token)
    {
        var edit = new ShadowGraphEdit(snapshot); var current = DraftingResources.Current(snapshot, "layer");
        foreach (var layer in Layers(snapshot))
        {
            token.ThrowIfCancellationRequested();
            var receipt = ReadReceipt(layer); if (receipt is null) continue;
            var flags = Flags(layer);
            foreach (var (bit, property) in Properties)
            {
                if ((receipt.Before & bit) == (receipt.Applied & bit) || (flags & bit) != (receipt.Applied & bit)) continue;
                var value = (receipt.Before & bit) != 0;
                if (bit == 2 && value && layer["Name"].Text.Equals(current, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Restoring isolation would freeze the current layer. Choose another current layer first.");
                edit.Set(layer.Id!.Value, property, ShadowValue.From(value));
            }
            ReplaceData(edit, layer, layer["XData"].Items.Where(p => !IsApplication(p["Key"].Text)).Select(p => p.Value));
        }
        edit.PruneUnreachable(); token.ThrowIfCancellationRequested();
        return new("RESTORE LAYER ISOLATION", edit.Build(), []);
    }

    private static ShadowReader[] Layers(ShadowSnapshot snapshot)
    {
        var layers = snapshot.Document["Layers"].Items.Take(MaximumLayers + 1).ToArray();
        if (layers.Length > MaximumLayers) throw new ArgumentException("Layer isolation is limited to 10000 layers.");
        return layers;
    }
    private static int Flags(ShadowReader layer) =>
        (layer["IsVisible"].Boolean(true) ? 1 : 0) | (layer["IsFrozen"].Boolean() ? 2 : 0) | (layer["IsLocked"].Boolean() ? 4 : 0);
    private static bool IsApplication(string value) => value.Equals(ApplicationName, StringComparison.OrdinalIgnoreCase);

    private static Receipt? ReadReceipt(ShadowReader layer)
    {
        var data = layer["XData"].Items.FirstOrDefault(p => IsApplication(p["Key"].Text))["Value"];
        if (!data.Exists) return null;
        var records = data["XDataRecord"].Items.Take(2).ToArray();
        var text = records.Length == 1 && records[0]["Code"].Text == "String" ? records[0]["Value"].Text : "";
        var parts = text.Length <= 32 ? text.Split('|') : [];
        if (parts.Length != 4 || parts[0] != "1" || parts[1] is not ("off" or "lock") ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var before) || before is < 0 or > 7 ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var applied) || applied is < 0 or > 7 || before == applied ||
            parts[2] != before.ToString(CultureInfo.InvariantCulture) || parts[3] != applied.ToString(CultureInfo.InvariantCulture) ||
            applied != 1 && applied != (parts[1] == "off" ? before & ~1 : before | 4) ||
            !IsApplication(data["ApplicationRegistry"]["Name"].Text))
            throw new ArgumentException("Layer '" + layer["Name"].Text + "' has invalid isolation metadata; no layer flags were changed.");
        return new(parts[1], before, applied);
    }
    private static (ObjectId Id, string Name) Registry(ShadowSnapshot snapshot, ShadowGraphEdit edit)
    {
        var registry = snapshot.Document["ApplicationRegistries"].Items.FirstOrDefault(a => IsApplication(a["Name"].Text));
        if (registry.Id is { } existing) return (existing, registry["Name"].Text);
        var id = edit.Create("netDxf.Tables.ApplicationRegistry", ShadowObjectKind.Table,
            new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(ApplicationName) });
        edit.ReplaceSequence(snapshot.Root, "ApplicationRegistries",
            snapshot.Document["ApplicationRegistries"].Items.Select(a => a.Value).Append(new ShadowValue.Reference(id)));
        return (id, ApplicationName);
    }
    private static void WriteReceipt(ShadowGraphEdit edit, ShadowReader layer, ObjectId registry, string registryName, Receipt receipt)
    {
        var record = edit.Create("netDxf.XDataRecord", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
        {
            ["Code"] = new ShadowValue.Atom("netDxf.XDataCode", "String"),
            ["Value"] = ShadowValue.From($"1|{receipt.Mode}|{receipt.Before}|{receipt.Applied}")
        });
        var data = edit.Create("netDxf.XData", ShadowObjectKind.Object, new Dictionary<string, ShadowValue>
        {
            ["ApplicationRegistry"] = new ShadowValue.Reference(registry),
            ["XDataRecord"] = new ShadowValue.Sequence([new ShadowValue.Reference(record)])
        });
        var pair = new ShadowValue.Structure("System.Collections.Generic.KeyValuePair`2",
            new Dictionary<string, ShadowValue> { ["Key"] = ShadowValue.From(registryName), ["Value"] = new ShadowValue.Reference(data) }.ToImmutableDictionary());
        ReplaceData(edit, layer, layer["XData"].Items.Where(p => !IsApplication(p["Key"].Text)).Select(p => p.Value).Append(pair));
    }
    private static void ReplaceData(ShadowGraphEdit edit, ShadowReader layer, IEnumerable<ShadowValue> entries)
    {
        var values = entries.ToImmutableArray();
        if (layer["XData"].Id is { } dictionary) edit.ReplaceSequence(dictionary, "$items", values);
        else
        {
            var id = edit.Create("netDxf.Collections.XDataDictionary", ShadowObjectKind.Object,
                new Dictionary<string, ShadowValue> { ["$items"] = new ShadowValue.Sequence(values) });
            edit.Set(layer.Id!.Value, "XData", new ShadowValue.Reference(id));
        }
    }
}
