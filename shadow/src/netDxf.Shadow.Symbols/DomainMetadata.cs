using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using netDxf.Tables;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Symbols;

/// <summary>Bounded, versioned ASCII JSON in ordinary DXF XDATA; never stores CLR type names or executable payloads.</summary>
public static class DomainMetadata
{
    public const string ApplicationName = "SHADOW_CAD";
    public const int MaximumBytes = 12000;
    private const string Header = "netdxf.domain.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16, Converters = { new VectorConverter() } };
    public static void Write(DxfObject target, DomainRecord value)
    {
        var data = new XData(new ApplicationRegistry(ApplicationName));
        foreach (var chunk in Chunks(value)) data.XDataRecord.Add(new XDataRecord(XDataCode.String, chunk));
        target.XData[ApplicationName] = data;
    }
    public static DomainRecord? Read(DxfObject target) => target.XData.TryGetValue(ApplicationName, out var data)
        ? Decode(data.XDataRecord.Select(r => r.Code == XDataCode.String && r.Value is string value ? value : throw new FormatException("Unexpected domain XDATA code."))) : null;
    public static DomainRecord? Read(ShadowReader entity)
    {
        var value = entity["XData"].Items.FirstOrDefault(p => p["Key"].Text == ApplicationName)["Value"];
        if (!value.Exists) return null;
        return Decode(value["XDataRecord"].Items.Select(r => r["Code"].Text == "String" ? r["Value"].Text : throw new FormatException("Unexpected domain XDATA code.")));
    }
    public static string[] Chunks(DomainRecord value)
    {
        value = value with { Ports = value.Ports.IsDefault ? [] : value.Ports, References = value.References.IsDefault ? [] : value.References };
        Validate(value); var text = JsonSerializer.Serialize(value, Json);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new ArgumentException("Domain metadata exceeds 12000 bytes.");
        return new[] { Header }.Concat(Enumerable.Range(0, (text.Length + 199) / 200).Select(i => text.Substring(i * 200, Math.Min(200, text.Length - i * 200)))).ToArray();
    }
    private static DomainRecord Decode(IEnumerable<string> values)
    {
        var chunks = values.Take(62).ToArray();
        if (chunks.Length is < 2 or > 61 || chunks[0] != Header || chunks.Skip(1).Any(v => v.Length > 200)) throw new FormatException("Invalid or oversized domain metadata envelope.");
        var text = string.Concat(chunks.Skip(1)); if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new FormatException("Domain metadata exceeds its byte limit.");
        try { var value = JsonSerializer.Deserialize<DomainRecord>(text, Json) ?? throw new FormatException("Empty domain metadata."); Validate(value); return value with { Properties = value.Properties?.WithComparers(StringComparer.OrdinalIgnoreCase), Ports = value.Ports.IsDefault ? [] : value.Ports, References = value.References.IsDefault ? [] : value.References }; }
        catch (Exception error) when (error is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException) { throw new FormatException("Invalid domain metadata: " + error.Message, error); }
    }
    public static void Validate(DomainRecord value)
    {
        ArgumentNullException.ThrowIfNull(value);
        static bool Text(string? value, int limit, bool empty = false) => value is not null && value.Length <= limit &&
            (empty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);
        static bool Point(Vec3 p) => p.IsFinite && p.Length <= 1e12;
        if (value.SchemaVersion != 1 || !Text(value.Module, 32) || !Text(value.Class, 64) || !Text(value.Key, 128) || !Text(value.Tag, 128, true))
            throw new ArgumentException("Domain metadata requires schema 1, module, class, stable key and a bounded tag.");
        if ((value.SymbolId is null) != (value.SymbolVersion is null) || value.SymbolId is not null &&
            (!Text(value.SymbolId, 128) || !Text(value.SymbolVersion, 32) || !Version.TryParse(value.SymbolVersion, out var version) || version.Build < 0 || version.Revision >= 0))
            throw new ArgumentException("Symbol references require an ID and explicit major.minor.patch version.");
        if (value.Properties?.Count > 64 || value.Properties?.Any(p => !Text(p.Key, 64) || !Text(p.Value, 1024, true)) == true ||
            value.Properties?.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Properties?.Count)
            throw new ArgumentException("Invalid or duplicate domain properties.");
        var ports = value.Ports.IsDefault ? [] : value.Ports;
        var references = value.References.IsDefault ? [] : value.References;
        if (ports.Length > 64 || references.Length > 64 || ports.Any(p => p is null || !Text(p.Id, 64) || !Text(p.Kind, 64) ||
            !Point(p.Position) || !Point(p.Direction) || p.NominalSize is not null && !Text(p.NominalSize, 64, true)) ||
            ports.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != ports.Length)
            throw new ArgumentException("Invalid domain ports.");
        if (references.Any(r => r is null || !Text(r.PortId, 64) || !Text(r.TargetKey, 128) || !Text(r.TargetPortId, 64) ||
            r.Role is not ("connection" or "association") || !ports.Any(p => p.Id == r.PortId)))
            throw new ArgumentException("References require declared source ports and bounded target keys, ports and supported roles.");
    }
    public static ImmutableArray<SymbolPort> WorldPorts(ShadowSnapshot snapshot, ShadowReader entity, DomainRecord data)
    {
        if (data.Ports.IsDefaultOrEmpty) return [];
        if (entity.Type == "Line")
        {
            var start = entity["StartPoint"].Vector(); var end = entity["EndPoint"].Vector(); var delta = end - start;
            var storedStart = data.Ports.FirstOrDefault(p => p.Id == "start")?.Position ?? start;
            var storedEnd = data.Ports.FirstOrDefault(p => p.Id == "end")?.Position ?? end;
            var storedDelta = storedEnd - storedStart;
            return data.Ports.Select(p => p with { Position = p.Id == "start" ? start : p.Id == "end" ? end :
                storedDelta.Length > 1e-12 ? start + delta * Vec3.Dot(p.Position - storedStart, storedDelta) / (storedDelta.Length * storedDelta.Length) : p.Position,
                Direction = p.Id is "start" or "end" && delta.Length > 1e-12 ? delta.Normalized * (p.Id == "start" ? -1 : 1) : p.Direction }).ToImmutableArray();
        }
        if (entity.Type != "Insert") return data.Ports;
        var block = entity["Block"];
        var unit = DrawingUnitConversion.Factor(block["Record"]["Units"].Text, snapshot.Document["DrawingVariables"]["InsUnits"].Text);
        var linear = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ)) * Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180) * Affine3.Scale(entity["Scale"].Vector(new(1,1,1)) * unit);
        var transform = Affine3.Move(entity["Position"].Vector()) * linear * Affine3.Move(-block["Origin"].Vector());
        return data.Ports.Select(p => p with { Position = transform.Point(p.Position), Direction = p.Direction.Length > 0 ? linear.Direction(p.Direction).Normalized : Vec3.Zero }).ToImmutableArray();
    }
    private sealed class VectorConverter : JsonConverter<Vec3>
    {
        public override Vec3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        { using var doc = JsonDocument.ParseValue(ref reader); var a = doc.RootElement; return new(a.GetProperty("x").GetDouble(), a.GetProperty("y").GetDouble(), a.GetProperty("z").GetDouble()); }
        public override void Write(Utf8JsonWriter writer, Vec3 value, JsonSerializerOptions options)
        { writer.WriteStartObject(); writer.WriteNumber("x", value.X); writer.WriteNumber("y", value.Y); writer.WriteNumber("z", value.Z); writer.WriteEndObject(); }
    }
}
