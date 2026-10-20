using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Drafting;

/// <summary>Portable, detached saved state. The mask and entry flags retain uninterpreted native bits.</summary>
public sealed record LayerStateTransfer(string Name, string Description, string CurrentLayer, bool PaperSpace,
    int RestoreMask, ImmutableArray<SavedLayerProperties> Entries);
public sealed record LayerStateResourceMap(string Source, string Target);
public sealed record LayerStateImportOptions(string? Name = null, bool Overwrite = false,
    ImmutableArray<LayerStateResourceMap> Layers = default, ImmutableArray<LayerStateResourceMap> Linetypes = default);
public sealed record LayerStateImportConflict(string Kind, string Source, string Message);
public sealed record LayerStateImportAnalysis(LayerStateTransfer State, bool ReplacesExisting,
    ImmutableArray<LayerStateImportConflict> Conflicts);

public static partial class DraftingLayerStates
{
    public static LayerStateTransfer Export(ShadowSnapshot snapshot, ObjectId id)
    {
        var detail = Details(snapshot, id); var state = detail.State;
        return new(state.Name, state.Description, state.CurrentLayer, state.PaperSpace, state.RestoreMask, detail.Entries);
    }

    /// <summary>Resolves explicit resource mappings without changing the snapshot. Missing resources are never fabricated.</summary>
    public static LayerStateImportAnalysis AnalyzeImport(ShadowSnapshot snapshot, LayerStateTransfer source, LayerStateImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(options);
        if (source.Entries.IsDefault || source.Entries.Length > 10000) throw new ArgumentException("LAS transfers require at most 10000 saved layers.");
        var name = options.Name ?? source.Name;
        var states = Manager(snapshot).Items.ToArray();
        if (states.Length > 1000) throw new ArgumentException("The saved-state manager exceeds its editing budget.");
        var existing = states.FirstOrDefault(s => s["Name"].Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        DraftingResources.ValidateName(name, [], null);
        if (source.Description is null || source.Description.Length > 4096 || source.Description.Any(char.IsControl))
            throw new ArgumentException("LAS descriptions require at most 4096 printable characters.");
        var layerNames = snapshot.Document["Layers"].Items.Select(l => l["Name"].Text).ToDictionary(n => n, StringComparer.OrdinalIgnoreCase);
        var lineNames = snapshot.Document["Linetypes"].Items.Select(l => l["Name"].Text)
            .Where(n => !n.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) && !n.Equals("ByBlock", StringComparison.OrdinalIgnoreCase)).ToDictionary(n => n, StringComparer.OrdinalIgnoreCase);
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source.Entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            DraftingResources.ValidateName(entry.Name, [], null); DraftingResources.ValidateName(entry.Linetype, [], null);
            if (!sourceNames.Add(entry.Name)) throw new ArgumentException("Imported layer names must be unique ignoring case.");
        }
        DraftingResources.ValidateName(source.CurrentLayer, [], null);
        var layerMaps = Maps(options.Layers, sourceNames.Append(source.CurrentLayer).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var lineMaps = Maps(options.Linetypes, source.Entries.Select(e => e.Linetype).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var conflicts = ImmutableArray.CreateBuilder<LayerStateImportConflict>();
        if (existing.Exists && !options.Overwrite) conflicts.Add(new("state", name, "A state with this name exists. Choose a new name or explicitly replace it."));
        if (!existing.Exists && states.Length == 1000) conflicts.Add(new("state", name, "The 1000-state editing budget is exhausted."));
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Resolve(string original, Dictionary<string, string> map, Dictionary<string, string> available, string kind)
        {
            var target = map.GetValueOrDefault(original, original);
            if (available.TryGetValue(target, out var canonical)) return canonical;
            if (reported.Add(kind + ":" + original)) conflicts.Add(new(kind, original, "Missing " + kind + " '" + target + "'. Map to an existing resource, or create/load it and preview again."));
            return target;
        }
        var entries = source.Entries.Select(e => e with
        {
            Name = Resolve(e.Name, layerMaps, layerNames, "layer"),
            Linetype = Resolve(e.Linetype, lineMaps, lineNames, "linetype")
        }).ToImmutableArray();
        if (entries.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            conflicts.Add(new("mapping", "", "Multiple saved layers map to the same drawing layer. Use one-to-one layer mappings."));
        var current = Resolve(source.CurrentLayer, layerMaps, layerNames, "layer");
        return new(source with { Name = existing.Exists ? existing["Name"].Text : name, CurrentLayer = current, Entries = entries }, existing.Exists, conflicts.ToImmutable());
    }

    /// <summary>Imports a validated saved state as one reversible plan, without applying its properties to drawing layers.</summary>
    public static DraftingPlan PlanImport(ShadowSnapshot snapshot, LayerStateTransfer source, LayerStateImportOptions options,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var analysis = AnalyzeImport(snapshot, source, options);
        if (!analysis.Conflicts.IsEmpty) throw new ArgumentException(string.Join(" ", analysis.Conflicts.Select(c => c.Message)));
        var value = analysis.State; var manager = Manager(snapshot);
        if (manager.Id is not { } managerId) throw new ArgumentException("No native layer-state manager.");
        var previous = manager.Items.FirstOrDefault(s => s["Name"].Text.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
        var edit = new ShadowGraphEdit(snapshot);
        var id = previous.Id ?? edit.Create("netDxf.Objects.LayerState", ShadowObjectKind.Table,
            new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(value.Name), ["Properties"] = new ShadowValue.Sequence([]) });
        if (!previous.Exists) edit.ReplaceSequence(managerId, "$items", manager.Items.Select(s => s.Value).Append(new ShadowValue.Reference(id)));
        edit.Set(id, "Description", ShadowValue.From(value.Description));
        edit.Set(id, "CurrentLayer", ShadowValue.From(value.CurrentLayer)); edit.Set(id, "PaperSpace", ShadowValue.From(value.PaperSpace));
        SetMask(edit, id, value.RestoreMask);
        var old = previous["Properties"].Items.ToDictionary(p => p["Key"].Text, p => p["Value"], StringComparer.OrdinalIgnoreCase);
        var pairs = new List<ShadowValue>();
        foreach (var entry in value.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (entry.ColorIndex is < -255 or > 256 || entry.Transparency is < -1 or > 100 || entry.Transparency is > 90 and < 100)
                throw new ArgumentException("Invalid saved color/transparency.");
            int? rgb = null;
            if (entry.TrueColor)
            {
                if (entry.Color is null || entry.Color.Length != 7 || entry.Color[0] != '#' ||
                    !int.TryParse(entry.Color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException("Saved true colors use #RRGGBB.");
                rgb = number;
            }
            old.TryGetValue(entry.Name, out var existing);
            var property = existing.Id ?? edit.Create("netDxf.Objects.LayerStateProperties", ShadowObjectKind.Object,
                new Dictionary<string, ShadowValue> { ["Name"] = ShadowValue.From(entry.Name) });
            ApplySavedAppearance(snapshot, edit, property, new(entry.Name, Linetype: entry.Linetype, Lineweight: entry.Lineweight));
            edit.Set(property, "Flags", new ShadowValue.Atom("netDxf.Objects.LayerPropertiesFlags", entry.Flags.ToString(CultureInfo.InvariantCulture)));
            edit.Set(property, "Color", ColorValue(edit, rgb, entry.ColorIndex));
            edit.Set(property, "Transparency", AlphaValue(edit, entry.Transparency, entry.StoredAlphaValue));
            // Existing source property names are already canonical to their resource.
            pairs.Add(Pair(existing.Exists ? existing["Name"].Text : entry.Name, new ShadowValue.Reference(property)));
        }
        edit.ReplaceSequence(id, "Properties", pairs); edit.PruneUnreachable();
        token.ThrowIfCancellationRequested();
        return new("IMPORT LAYER STATE", edit.Build(), []);
    }
    private static Dictionary<string, string> Maps(ImmutableArray<LayerStateResourceMap> maps, HashSet<string> sources)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (maps.IsDefault) return result;
        if (maps.Length > 10000) throw new ArgumentException("Resource mapping budget exceeded.");
        foreach (var map in maps)
        {
            ArgumentNullException.ThrowIfNull(map);
            if (map.Source is null || !sources.Contains(map.Source) || !result.TryAdd(map.Source, map.Target))
                throw new ArgumentException("Map each referenced source resource at most once.");
            DraftingResources.ValidateName(map.Target, [], null);
        }
        return result;
    }
}
