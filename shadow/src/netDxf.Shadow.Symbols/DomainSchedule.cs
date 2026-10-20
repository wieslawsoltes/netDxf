using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record DomainScheduleOptions(string Query = "", string Class = "", string GroupBy = "class",
    string? GroupValue = null, string SortBy = "tag", bool Descending = false, int MaximumObjects = 10000);
public sealed record DomainScheduleRow(ObjectId Entity, string Type, string Class, string Key, string Tag,
    string? SymbolId, string? SymbolVersion, ImmutableDictionary<string, string> Properties,
    ImmutableArray<SymbolPort> Ports, ImmutableArray<DomainReference> References, double? SegmentLength);
public sealed record DomainScheduleGroup(string Value, int Objects, int Routes, int MeasuredSegments, double SegmentLength);
public sealed record DomainScheduleIssue(ObjectId Entity, string Message);

/// <summary>
/// Immutable schedules of explicit discipline metadata on layout roots, including hidden layers.
/// Counts and straight source-segment lengths describe the retained scan, never inferred connectivity or purchased quantities.
/// </summary>
public sealed record DomainSchedule(Guid DocumentId, long Revision, string Module, string Layout, string DrawingUnits,
    DomainScheduleOptions Options, bool ScanComplete, int ObjectsVisited, int RecordsScanned, int InvalidRecords,
    ImmutableArray<string> Limitations, ImmutableArray<DomainScheduleRow> Rows,
    ImmutableArray<DomainScheduleGroup> Groups, ImmutableArray<DomainScheduleIssue> Issues)
{
    public const int MaximumRecords = 10000;
    public const int MaximumDataCharacters = 16 * 1024 * 1024;
    public bool Complete => ScanComplete && InvalidRecords == 0;

    public static DomainSchedule Build(ShadowSnapshot snapshot, string module, string layout = "Model",
        DomainScheduleOptions? options = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new();
        if (string.IsNullOrWhiteSpace(module) || module.Length > 64 || !snapshot.Layout(layout).Exists)
            throw new ArgumentException("Choose a discipline and an existing layout.");
        if (options.Query is null || options.Query.Length > 256 || options.Class is null || options.Class.Length > 128 ||
            options.GroupValue?.Length > 8192 || options.MaximumObjects is < 1 or > 100000)
            throw new ArgumentException("Invalid schedule search or scan bounds.");
        ValidateField(options.GroupBy); ValidateField(options.SortBy);
        token.ThrowIfCancellationRequested();
        var rows = new List<DomainScheduleRow>(); var issues = ImmutableArray.CreateBuilder<DomainScheduleIssue>();
        var limitations = ImmutableArray.CreateBuilder<string>();
        var visited = 0; var characters = 0; var invalid = 0; var complete = true;
        foreach (var entity in snapshot.DrawingEntities(layout))
        {
            token.ThrowIfCancellationRequested();
            if (visited == options.MaximumObjects) { complete = false; limitations.Add("Layout root scan limit reached."); break; }
            visited++;
            if (entity.Id is not { } id) continue;
            try
            {
                var data = DomainMetadata.Read(entity);
                if (data is null || data.Module != module) continue;
                var properties = (data.Properties ?? ImmutableDictionary<string, string>.Empty).WithComparers(StringComparer.OrdinalIgnoreCase);
                var size = data.Key.Length + data.Tag.Length + data.Class.Length + (data.SymbolId?.Length ?? 0) + (data.SymbolVersion?.Length ?? 0)
                    + properties.Sum(p => p.Key.Length + p.Value.Length)
                    + data.Ports.Sum(p => p.Id.Length + p.Kind.Length + (p.NominalSize?.Length ?? 0))
                    + data.References.Sum(r => r.PortId.Length + r.TargetKey.Length + r.TargetPortId.Length + r.Role.Length);
                if (rows.Count == MaximumRecords || size > MaximumDataCharacters - characters)
                { complete = false; limitations.Add("Schedule record or retained-text budget reached."); break; }
                var ports = DomainMetadata.WorldPorts(snapshot, entity, data);
                if (ports.Any(p => !p.Position.IsFinite || p.Position.Length > 1e12 || !p.Direction.IsFinite))
                    throw new ArgumentException("Transformed port coordinates exceed the supported finite drawing bounds.");
                double? length = null;
                if (entity.Type == "Line" && data.Class is "pipe" or "wire" or "signal")
                {
                    var a = entity["StartPoint"].Vector(); var b = entity["EndPoint"].Vector();
                    var measured = Vec3.Distance(a, b);
                    if (a.IsFinite && b.IsFinite && double.IsFinite(measured)) length = measured;
                }
                rows.Add(new(id, entity.Type, data.Class, data.Key, data.Tag, data.SymbolId, data.SymbolVersion,
                    properties, ports, data.References, length));
                characters += size;
            }
            catch (Exception error) when (error is FormatException or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                invalid++;
                if (issues.Count < 100) issues.Add(new(id, error.Message.Length <= 512 ? error.Message : error.Message[..512]));
            }
        }
        if (invalid > 0) limitations.Add($"{invalid} layout roots have unreadable discipline metadata; module membership cannot be established for those roots.");
        var records = rows.Count;
        var filtered = new List<DomainScheduleRow>();
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            if (options.Class.Length > 0 && !row.Class.Equals(options.Class, StringComparison.OrdinalIgnoreCase)) continue;
            if (options.GroupValue is not null && !Field(row, options.GroupBy).Equals(options.GroupValue, StringComparison.OrdinalIgnoreCase)) continue;
            if (options.Query.Length > 0 && !Matches(row, options.Query)) continue;
            filtered.Add(row);
        }
        var sorted = filtered.OrderBy(r => Field(r, options.SortBy), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => Field(r, options.SortBy), StringComparer.Ordinal).ThenBy(r => r.Entity.Value, StringComparer.Ordinal);
        var ordered = (options.Descending ? sorted.Reverse() : sorted).ToImmutableArray();
        var groups = ImmutableArray.CreateBuilder<DomainScheduleGroup>();
        foreach (var group in ordered.GroupBy(r => Field(r, options.GroupBy), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            var canonical = group.Select(r => Field(r, options.GroupBy)).Min(StringComparer.Ordinal)!;
            groups.Add(new(canonical, group.Count(), group.Where(r => r.Properties.GetValueOrDefault("route", "").Length > 0)
                .Select(r => r.Properties["route"]).Distinct(StringComparer.Ordinal).Count(),
                group.Count(r => r.SegmentLength.HasValue), group.Sum(r => r.SegmentLength ?? 0)));
        }
        return new(snapshot.DocumentId, snapshot.Revision, module, layout, snapshot.Document["DrawingVariables"]["InsUnits"].Text,
            options, complete, visited, records, invalid, limitations.ToImmutable(), ordered, groups.ToImmutable(), issues.ToImmutable());
    }
    public static string Field(DomainScheduleRow row, string name) => name.ToLowerInvariant() switch
    {
        "tag" => row.Tag, "class" => row.Class, "entity" => row.Entity.Value, "key" => row.Key, "type" => row.Type,
        "symbolid" => row.SymbolId ?? "", "symbolversion" => row.SymbolVersion ?? "",
        _ => row.Properties.GetValueOrDefault(name, "")
    };
    private static bool Matches(DomainScheduleRow row, string query) =>
        new[] { row.Tag, row.Class, row.Key, row.Entity.Value, row.SymbolId ?? "", row.SymbolVersion ?? "" }
            .Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        row.Properties.Any(p => p.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Value.Contains(query, StringComparison.OrdinalIgnoreCase));
    private static void ValidateField(string field)
    {
        if (string.IsNullOrWhiteSpace(field) || field.Length > 128 || field.Any(char.IsControl))
            throw new ArgumentException("Schedule fields require 1–128 printable characters.");
    }
}
