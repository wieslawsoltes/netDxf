using System.Collections.Immutable;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Shadow.Symbols;

/// <summary>Native block/ATTRIB authoring for applications that own a mutable DXF document.</summary>
public static class SymbolNative
{
    public static string BlockName(SymbolDefinition symbol) => "SYM_" + symbol.Id + "_v" + symbol.Version;
    public static Block CreateBlock(SymbolDefinition symbol)
    {
        _ = new SymbolCatalog([symbol]);
        var block = new Block(BlockName(symbol)) { Origin = V(symbol.BasePoint), Description = symbol.Description };
        block.Record.Units = Enum.Parse<DrawingUnits>(symbol.Units);
        foreach (var shape in symbol.Geometry)
        {
            EntityObject entity = shape.Kind switch
            {
                SymbolStrokeKind.Line => new Line(V(shape.Start), V(shape.End)),
                SymbolStrokeKind.Circle => new Circle(V(shape.Start), shape.Radius),
                SymbolStrokeKind.Arc => new Arc(V(shape.Start), shape.Radius, shape.StartAngle, shape.EndAngle),
                SymbolStrokeKind.Ellipse => new Ellipse(V(shape.Start), shape.Radius * 2, shape.End.X * 2),
                SymbolStrokeKind.Text => new Text(shape.Text, V(shape.Start), shape.Radius),
                _ => throw new NotSupportedException()
            };
            entity.Color = AciColor.ByBlock; entity.Lineweight = Lineweight.ByBlock; block.Entities.Add(entity);
        }
        foreach (var attribute in symbol.Attributes)
            block.AttributeDefinitions.Add(new AttributeDefinition(attribute.Id)
            { Value = attribute.Default, Position = V(attribute.Position), Height = attribute.Height,
                Flags = attribute.Visible ? AttributeFlags.None : AttributeFlags.Hidden, Color = AciColor.ByBlock });
        DomainMetadata.Write(block, new("symbols", "definition", symbol.Id + "@" + symbol.Version, symbol.Name,
            symbol.Id, symbol.Version, Properties: DefinitionProperties(symbol), Ports: symbol.Ports));
        return block;
    }
    public static Insert Place(DxfDocument document, SymbolDefinition symbol, Vec3 position, string module, string tag,
        IReadOnlyDictionary<string,string>? properties = null, double rotation = 0, double scale = 1, string layer = "0", string? key = null)
    {
        CheckPlacement(position, rotation, scale); var values = Attributes(symbol, tag, properties);
        var name = BlockName(symbol); var block = document.Blocks.TryGetValue(name, out var existing) ? existing : CreateBlock(symbol);
        var definition = DomainMetadata.Read(block);
        if (definition?.SymbolId != symbol.Id || definition.SymbolVersion != symbol.Version) throw new InvalidOperationException("The symbol block name already belongs to another definition.");
        var insert = new Insert(block, V(position)) { Rotation = rotation, Scale = new(scale), Layer = document.Layers.TryGetValue(layer, out var found) ? found : throw new ArgumentException("Unknown insertion layer.") };
        foreach (var attr in insert.Attributes) if (values.TryGetValue(attr.Tag, out var value)) attr.Value = value;
        DomainMetadata.Write(insert, new(module, symbol.Category, key ?? Guid.NewGuid().ToString("N"), tag,
            symbol.Id, symbol.Version, values.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), Ports(symbol.Ports, values), []));
        document.Entities.Add(insert); insert.TransformAttributes(); return insert;
    }
    internal static Dictionary<string,string> Attributes(SymbolDefinition symbol, string tag, IReadOnlyDictionary<string,string>? supplied)
    {
        var values = symbol.Attributes.ToDictionary(a => a.Id, a => a.Default, StringComparer.OrdinalIgnoreCase);
        if (supplied is not null) foreach (var (name,value) in supplied) values[name] = value;
        values["TAG"] = tag;
        if (symbol.Attributes.Any(a => a.Required && string.IsNullOrWhiteSpace(values.GetValueOrDefault(a.Id)))) throw new ArgumentException("Required symbol attributes are missing.");
        return values;
    }
    internal static ImmutableArray<SymbolPort> Ports(ImmutableArray<SymbolPort> ports, IReadOnlyDictionary<string,string> values) =>
        ports.Select(p => p with { NominalSize = values.TryGetValue(p.Id + "Size", out var specific) ? specific :
            values.TryGetValue("nominalSize", out var nominal) ? nominal : values.TryGetValue("DN", out var dn) ? dn : p.NominalSize }).ToImmutableArray();
    internal static ImmutableDictionary<string,string> DefinitionProperties(SymbolDefinition symbol) =>
        ImmutableDictionary<string,string>.Empty.Add("units", symbol.Units).Add("standards", string.Join("; ", symbol.Standards));
    internal static void CheckPlacement(Vec3 point, double angle, double scale)
    { if (!point.IsFinite || point.Length > 1e12 || !double.IsFinite(angle) || !double.IsFinite(scale) || scale is <= 1e-9 or > 1e9) throw new ArgumentException("Invalid symbol placement, rotation or positive uniform scale."); }
    private static Vector3 V(Vec3 point) => new(point.X, point.Y, point.Z);
}
