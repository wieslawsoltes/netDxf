using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Symbols;

/// <summary>Pure undoable symbol and metadata plans. All native construction is deferred to the editor transaction.</summary>
public static class SymbolAuthoring
{
    public static DraftingPlan Place(ShadowSnapshot snapshot, SymbolDefinition symbol, Vec3 position, string module, string tag,
        IReadOnlyDictionary<string,string>? properties = null, string layout = "Model", double rotation = 0, double scale = 1,
        string layer = "0", CancellationToken cancellationToken = default)
    {
        SymbolNative.CheckPlacement(position, rotation, scale); _ = new SymbolCatalog([symbol]);
        var values = SymbolNative.Attributes(symbol, tag, properties);
        var owner = snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown layout.");
        var layerValue = snapshot.Document["Layers"].Items.FirstOrDefault(l => l["Name"].Text == layer);
        if (!layerValue.Exists || layerValue["IsLocked"].Boolean() || layerValue["IsFrozen"].Boolean()) throw new ArgumentException("Choose an existing unlocked, thawed insertion layer.");
        var edit = new ShadowGraphEdit(snapshot); var blockName = SymbolNative.BlockName(symbol);
        var blockValue = snapshot.Document["Blocks"].Items.FirstOrDefault(b => b["Name"].Text == blockName);
        ObjectId block;
        var definitions = new Dictionary<string,ObjectId>(StringComparer.OrdinalIgnoreCase);
        // Native symbols use ByBlock paint so an INSERT's explicit color/weight
        // reaches its strokes and attributes, including in a newly built definition.
        var byBlockColor = R(edit.Create("netDxf.AciColor", ShadowObjectKind.Object,
            P(("Index", new ShadowValue.Atom("System.Int16", "0")))));
        var style = snapshot.Document["TextStyles"].Items.First(s => s["Name"].Text == "Standard").Value;
        if (blockValue.Id is { } existing)
        {
            var info = DomainMetadata.Read(blockValue);
            if (info?.SymbolId != symbol.Id || info.SymbolVersion != symbol.Version) throw new InvalidOperationException("The symbol block name belongs to another definition.");
            block = existing;
            foreach (var item in blockValue["AttributeDefinitions"].Items)
            {
                var definition = item["Value"].Exists ? item["Value"] : item;
                if (definition.Id is { } id) definitions[definition["Tag"].Text] = id;
            }
        }
        else
        {
            block = edit.Create("netDxf.Blocks.Block", ShadowObjectKind.Block, P(("Name", S(blockName)), ("Origin", V(symbol.BasePoint)), ("Description", S(symbol.Description)), ("Entities", new ShadowValue.Sequence([]))));
            var record = edit.Create("netDxf.Blocks.BlockRecord", ShadowObjectKind.Object, P(("Name", S(blockName)), ("Units", new ShadowValue.Atom("netDxf.Units.DrawingUnits", symbol.Units))));
            edit.Set(block, "Record", R(record));
            edit.ReplaceSequence(snapshot.Root, "Blocks", snapshot.Document["Blocks"].Items.Select(b => b.Value).Append(R(block)));
            foreach (var shape in symbol.Geometry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var props = shape.Kind switch
                {
                    SymbolStrokeKind.Line => P(("StartPoint", V(shape.Start)), ("EndPoint", V(shape.End))),
                    SymbolStrokeKind.Circle => P(("Center", V(shape.Start)), ("Radius", N(shape.Radius))),
                    SymbolStrokeKind.Arc => P(("Center", V(shape.Start)), ("Radius", N(shape.Radius)), ("StartAngle", N(shape.StartAngle)), ("EndAngle", N(shape.EndAngle))),
                    SymbolStrokeKind.Ellipse => P(("Center", V(shape.Start)), ("MajorAxis", N(shape.Radius * 2)), ("MinorAxis", N(shape.End.X * 2))),
                    SymbolStrokeKind.Text => P(("Position", V(shape.Start)), ("Value", S(shape.Text)), ("Height", N(shape.Radius)), ("Style", style)),
                    _ => throw new NotSupportedException()
                };
                props["Color"] = byBlockColor;
                props["Lineweight"] = new ShadowValue.Atom("netDxf.Lineweight", "ByBlock");
                edit.AddEntity(block, "netDxf.Entities." + shape.Kind, props);
            }
            foreach (var a in symbol.Attributes)
                definitions[a.Id] = edit.Create("netDxf.Entities.AttributeDefinition", ShadowObjectKind.Object,
                    P(("Tag", S(a.Id)), ("Value", S(a.Default)), ("Position", V(a.Position)), ("Height", N(a.Height)), ("Style", style), ("Flags", Flag(a.Visible)), ("Color", byBlockColor)));
            edit.Set(block, "AttributeDefinitions", new ShadowValue.Sequence(definitions.Select(a => (ShadowValue)new ShadowValue.Structure(
                typeof(KeyValuePair<string,netDxf.Entities.AttributeDefinition>).FullName!, P(("Key", S(a.Key)), ("Value", R(a.Value))).ToImmutableDictionary())).ToImmutableArray()));
        }
        var unit = DrawingUnitConversion.Factor(symbol.Units, snapshot.Document["DrawingVariables"]["InsUnits"].Text);
        var transform = Affine3.Move(position) * Affine3.RotateZ(rotation * Math.PI / 180) * Affine3.Scale(new(scale * unit, scale * unit, scale * unit)) * Affine3.Move(-symbol.BasePoint);
        var attributes = symbol.Attributes.Select(a => R(edit.Create("netDxf.Entities.Attribute", ShadowObjectKind.Object,
            P(("Definition", R(definitions[a.Id])), ("Value", S(values.GetValueOrDefault(a.Id, a.Default))), ("Position", V(transform.Point(a.Position))),
                ("Height", N(a.Height * scale * unit)), ("Rotation", N(rotation)), ("Style", style), ("Flags", Flag(a.Visible)),
                ("Color", blockValue.Exists ? snapshot.Read(definitions[a.Id])["Color"].Value : byBlockColor))))).ToImmutableArray();
        var insert = edit.AddEntity(owner, "netDxf.Entities.Insert", P(("Block", R(block)), ("Position", V(position)), ("Rotation", N(rotation)),
            ("Scale", V(new(scale,scale,scale))), ("Normal", V(Vec3.UnitZ)), ("Layer", layerValue.Value), ("Attributes", new ShadowValue.Sequence(attributes))));
        var working = edit.Build().Apply(snapshot);
        if (!blockValue.Exists) working = Attach(working, block, new("symbols", "definition", symbol.Id + "@" + symbol.Version, symbol.Name, symbol.Id, symbol.Version, Properties: SymbolNative.DefinitionProperties(symbol), Ports: symbol.Ports) { Passages = symbol.Passages }).Patch.Apply(working);
        working = Attach(working, insert, new(module, symbol.Category, Guid.NewGuid().ToString("N"), tag, symbol.Id, symbol.Version, values.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), SymbolNative.Ports(symbol.Ports, values), []) { Passages = symbol.Passages }).Patch.Apply(working);
        cancellationToken.ThrowIfCancellationRequested();
        return new("Place " + symbol.Name, ShadowGraphPatch.Between(snapshot, working), [insert]);
    }
    public static DraftingPlan Attach(ShadowSnapshot snapshot, ObjectId target, DomainRecord data)
    {
        var chunks = DomainMetadata.Chunks(data); var entity = snapshot.Read(target);
        if (!entity.Exists) throw new ArgumentException("Unknown metadata target.");
        var edit = new ShadowGraphEdit(snapshot);
        var existingRegistry = snapshot.Document["ApplicationRegistries"].Items.FirstOrDefault(a => a["Name"].Text.Equals(DomainMetadata.ApplicationName, StringComparison.OrdinalIgnoreCase));
        var registry = existingRegistry.Id;
        var registryName = registry is null ? DomainMetadata.ApplicationName : existingRegistry["Name"].Text;
        if (registry is null)
        {
            registry = edit.Create("netDxf.Tables.ApplicationRegistry", ShadowObjectKind.Table, P(("Name", S(DomainMetadata.ApplicationName))));
            edit.ReplaceSequence(snapshot.Root, "ApplicationRegistries", snapshot.Document["ApplicationRegistries"].Items.Select(a => a.Value).Append(R(registry.Value)));
        }
        var records = chunks.Select(chunk => R(edit.Create("netDxf.XDataRecord", ShadowObjectKind.Object, P(("Code", new ShadowValue.Atom("netDxf.XDataCode", "String")), ("Value", S(chunk)))))).ToImmutableArray();
        var value = edit.Create("netDxf.XData", ShadowObjectKind.Object, P(("ApplicationRegistry", R(registry.Value)), ("XDataRecord", new ShadowValue.Sequence(records))));
        // Native XDATA keys use the registered name's exact spelling. Retain
        // that key and its position between unrelated application payloads.
        var pair = new ShadowValue.Structure(typeof(KeyValuePair<string,XData>).FullName!, P(("Key", S(registryName)), ("Value", R(value))).ToImmutableDictionary());
        var replaced = false;
        var pairs = entity["XData"].Items.Select(p =>
        {
            if (!p["Key"].Text.Equals(DomainMetadata.ApplicationName, StringComparison.OrdinalIgnoreCase)) return p.Value;
            replaced = true; return pair;
        }).ToImmutableArray();
        if (!replaced) pairs = pairs.Add(pair);
        if (entity["XData"].Id is { } existingDictionary) edit.ReplaceSequence(existingDictionary, "$items", pairs);
        else
        {
            var dictionary = edit.Create("netDxf.Collections.XDataDictionary", ShadowObjectKind.Object, P(("$items", new ShadowValue.Sequence(pairs))));
            edit.Set(target, "XData", R(dictionary));
        }
        edit.PruneUnreachable();
        return new("Update domain metadata", edit.Build(), []);
    }
    private static ShadowValue Flag(bool visible) => new ShadowValue.Atom("netDxf.Entities.AttributeFlags", visible ? "None" : "Hidden");
    internal static Dictionary<string,ShadowValue> P(params (string Key, ShadowValue Value)[] fields) => fields.ToDictionary(p => p.Key, p => p.Value);
    internal static ShadowValue S(string value) => ShadowValue.From(value);
    internal static ShadowValue N(double value) => ShadowValue.From(value);
    internal static ShadowValue R(ObjectId value) => new ShadowValue.Reference(value);
    internal static ShadowValue V(Vec3 value) => new ShadowValue.Structure("netDxf.Vector3", P(("X", N(value.X)), ("Y", N(value.Y)), ("Z", N(value.Z))).ToImmutableDictionary());
}
