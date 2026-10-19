using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record SymbolAttribute(string Id, string Name, string Default = "", bool Required = false,
    bool Visible = false, Vec3 Position = default, double Height = 2.5);
public sealed record SymbolPort(string Id, string Kind, Vec3 Position, Vec3 Direction = default, string? NominalSize = null);
public enum SymbolStrokeKind { Line, Circle, Arc, Ellipse, Text }
public sealed record SymbolStroke(SymbolStrokeKind Kind, Vec3 Start, Vec3 End = default,
    double Radius = 0, double StartAngle = 0, double EndAngle = 360, string Text = "");
public sealed record SymbolDefinition(string Id, string Version, string Name, string Description, string Category,
    string Units, Vec3 BasePoint, ImmutableArray<string> Standards, ImmutableArray<SymbolAttribute> Attributes,
    ImmutableArray<SymbolPort> Ports, ImmutableArray<SymbolStroke> Geometry);
public sealed record SymbolCatalogPage(int Offset, int Limit, int Total, ImmutableArray<SymbolDefinition> Items);
public sealed record DomainReference(string PortId, string TargetKey, string TargetPortId, string Role = "connection");
/// <summary>Portable identity and explicit domain relations. Keys are stable IDs, never DXF handles or shadow IDs.</summary>
public sealed record DomainRecord(string Module, string Class, string Key, string Tag,
    string? SymbolId = null, string? SymbolVersion = null, ImmutableDictionary<string, string>? Properties = null,
    ImmutableArray<SymbolPort> Ports = default, ImmutableArray<DomainReference> References = default, int SchemaVersion = 1);
public sealed record DomainEntity(ObjectId Entity, DomainRecord Data, ImmutableArray<SymbolPort> WorldPorts);

/// <summary>An immutable versioned catalog. Resolution never silently substitutes another version.</summary>
public sealed class SymbolCatalog
{
    private readonly ImmutableArray<SymbolDefinition> symbols;
    public SymbolCatalog(IEnumerable<SymbolDefinition> definitions)
    {
        symbols = definitions.Take(10001).Select(Validate).OrderBy(s => s.Id, StringComparer.Ordinal).ThenBy(s => Version.Parse(s.Version)).ToImmutableArray();
        if (symbols.Length > 10000 || symbols.GroupBy(s => (s.Id, s.Version)).Any(g => g.Count() != 1)) throw new ArgumentException("Symbol catalog contains duplicate versions or exceeds 10000 definitions.");
    }
    public SymbolDefinition Resolve(string id, string? version = null)
    {
        var matches = symbols.Where(s => s.Id == id && (version is null || s.Version == version));
        return matches.LastOrDefault() ?? throw new ArgumentException("Unknown symbol ID or version.");
    }
    public SymbolCatalogPage Search(string query = "", int offset = 0, int limit = 50)
    {
        if (query.Length > 256 || offset < 0 || limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        var result = symbols.Where(s => string.Join(' ', s.Id, s.Name, s.Description, s.Category, string.Join(' ', s.Standards)).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return new(offset, limit, result.Length, result.Skip(offset).Take(limit).ToImmutableArray());
    }
    private static SymbolDefinition Validate(SymbolDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 128 || !Version.TryParse(definition.Version, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new ArgumentException("Symbols require an ID and explicit major.minor.patch numeric version.");
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 128 || definition.Description is null || definition.Description.Length > 1024 ||
            string.IsNullOrWhiteSpace(definition.Category) || definition.Category.Length > 64 || definition.Standards.IsDefault || definition.Standards.Length > 16 || definition.Standards.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 128))
            throw new ArgumentException("Symbol names, descriptions and standards references must be bounded.");
        DomainMetadata.Validate(new("symbols", definition.Category, definition.Id, definition.Name, definition.Id, definition.Version, Ports: definition.Ports));
        if (definition.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'))) throw new ArgumentException("Symbol IDs use ASCII letters, digits, dot, hyphen and underscore.");
        if (!Enum.TryParse<netDxf.Units.DrawingUnits>(definition.Units, out var units) || !Enum.IsDefined(units)) throw new ArgumentException("Unknown symbol units.");
        if ((!definition.BasePoint.IsFinite || definition.BasePoint.Length > 1e12) || definition.Geometry.IsDefaultOrEmpty || definition.Geometry.Length > 512 || definition.Ports.IsDefault || definition.Ports.Length > 64 || definition.Attributes.IsDefault || definition.Attributes.Length > 64)
            throw new ArgumentException("Symbol geometry, attributes or ports exceed their supported bounds.");
        if (definition.Ports.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != definition.Ports.Length || definition.Attributes.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Attributes.Length)
            throw new ArgumentException("Symbol port IDs and attribute tags must be unique.");
        foreach (var port in definition.Ports) if (string.IsNullOrWhiteSpace(port.Id) || !port.Position.IsFinite || !port.Direction.IsFinite) throw new ArgumentException("Invalid symbol port.");
        foreach (var stroke in definition.Geometry) if (!stroke.Start.IsFinite || stroke.Start.Length > 1e12 || !stroke.End.IsFinite || stroke.End.Length > 1e12 || !double.IsFinite(stroke.Radius) || !double.IsFinite(stroke.StartAngle) || !double.IsFinite(stroke.EndAngle) || !Enum.IsDefined(stroke.Kind) ||
            stroke.Kind is SymbolStrokeKind.Circle or SymbolStrokeKind.Arc or SymbolStrokeKind.Ellipse && stroke.Radius <= 0 || stroke.Kind == SymbolStrokeKind.Ellipse && (stroke.End.X <= 0 || stroke.End.X > stroke.Radius) || stroke.Kind == SymbolStrokeKind.Text && (stroke.Radius <= 0 || stroke.Text is null || stroke.Text.Length > 1024)) throw new ArgumentException("Invalid symbol geometry.");
        foreach (var attr in definition.Attributes) if (string.IsNullOrWhiteSpace(attr.Id) || attr.Id.Length > 64 || attr.Name is null || attr.Name.Length > 128 || attr.Default is null || attr.Default.Length > 1024 || attr.Id.Any(char.IsWhiteSpace) || !attr.Position.IsFinite || !double.IsFinite(attr.Height) || attr.Height <= 0) throw new ArgumentException("Invalid symbol attribute.");
        return definition;
    }
}
