using System.Collections.Immutable;
using System.Text.Json;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Studio;

public sealed record CadModuleScheduleRequest(long Revision, string Module, DomainScheduleOptions? Options = null,
    string? ScheduleKey = null, int RowOffset = 0, int GroupOffset = 0, int IssueOffset = 0, int Limit = 50);
public sealed record CadSchedulePage<T>(int Total, int Offset, ImmutableArray<T> Items, int? NextOffset);
public sealed record CadModuleScheduleResult(string ScheduleKey, Guid DocumentId, long Revision, string WorkspaceId,
    long ViewVersion, string Layout, string Module, string DrawingUnits, DomainScheduleOptions Options, bool Complete,
    bool ScanComplete, int ObjectsVisited, int RecordsScanned, int InvalidRecords, ImmutableArray<string> Limitations,
    CadSchedulePage<DomainScheduleRow> Rows, CadSchedulePage<DomainScheduleGroup> Groups, CadSchedulePage<DomainScheduleIssue> Issues);

public sealed partial class Studio
{
    private sealed record ScheduleCache(ShadowSnapshot Snapshot, string Workspace, long View, long Configuration,
        string Layout, string Scope, string Key, DomainSchedule Schedule);
    private ScheduleCache? scheduleCache;

    public CadModuleScheduleResult CadModuleSchedule(CadModuleScheduleRequest request, CancellationToken token = default) =>
        ModuleSchedule(request, 131072, token);
    private CadModuleScheduleResult ModuleSchedule(CadModuleScheduleRequest request, int maximumCharacters, CancellationToken token)
    {
        lock (gate)
        {
            ArgumentNullException.ThrowIfNull(request); Check(request.Revision); token.ThrowIfCancellationRequested();
            if (!cadModules.IsEnabled(request.Module)) throw new InvalidOperationException("Enable this discipline before reading its schedule.");
            if (activeBranch?.BlockRoot is not null) throw new InvalidOperationException("Finish block editing before reading layout discipline schedules.");
            if (request.Limit is < 1 or > 200 || request.RowOffset < 0 || request.GroupOffset < 0 || request.IssueOffset < 0 || request.ScheduleKey?.Length > 128)
                throw new ArgumentException("Invalid schedule page or continuation key.");
            var options = request.Options ?? new();
            var scope = JsonSerializer.Serialize(new { request.Module, options }, McpJson.Options);
            var cached = scheduleCache;
            var valid = cached is not null && ReferenceEquals(cached.Snapshot, Current) && cached.Workspace == WorkspaceId &&
                cached.View == viewVersion && cached.Configuration == moduleConfigurationVersion && cached.Layout == layout && cached.Scope == scope;
            if (request.ScheduleKey is not null && (!valid || request.ScheduleKey != cached!.Key))
                throw new ShadowConflictException("The schedule changed or belongs to another query. Refresh its first page.");
            if (request.ScheduleKey is null && (request.RowOffset != 0 || request.GroupOffset != 0 || request.IssueOffset != 0))
                throw new ArgumentException("Schedule continuation pages require their original key.");
            if (!valid)
            {
                var schedule = DomainSchedule.Build(Current, request.Module, layout, options, token);
                scheduleCache = cached = new(Current, WorkspaceId, viewVersion, moduleConfigurationVersion, layout, scope, Guid.NewGuid().ToString("N"), schedule);
            }
            var value = cached!.Schedule;
            var result = new CadModuleScheduleResult(cached.Key, value.DocumentId, value.Revision, WorkspaceId, viewVersion,
                layout, request.Module, value.DrawingUnits, value.Options, value.Complete, value.ScanComplete, value.ObjectsVisited,
                value.RecordsScanned, value.InvalidRecords, value.Limitations,
                SchedulePage(value.Rows, request.RowOffset, request.Limit), SchedulePage(value.Groups, request.GroupOffset, request.Limit),
                SchedulePage(value.Issues, request.IssueOffset, request.Limit));
            while (JsonSerializer.Serialize(result, McpJson.Options).Length > maximumCharacters)
            {
                token.ThrowIfCancellationRequested();
                if (result.Rows.Items.Length <= 1 && result.Groups.Items.Length <= 1 && result.Issues.Items.Length <= 1)
                    throw new InvalidOperationException("The minimum schedule page exceeds this host's response budget.");
                result = result with { Rows = ScheduleShrink(result.Rows), Groups = ScheduleShrink(result.Groups), Issues = ScheduleShrink(result.Issues) };
            }
            return result;
        }
    }
    private static CadSchedulePage<T> SchedulePage<T>(ImmutableArray<T> source, int offset, int limit)
    {
        if (offset > source.Length) throw new ArgumentException("Schedule page offset exceeds the retained stream.");
        var items = source.Skip(offset).Take(limit).ToImmutableArray();
        return new(source.Length, offset, items, offset + items.Length < source.Length ? offset + items.Length : null);
    }
    private static CadSchedulePage<T> ScheduleShrink<T>(CadSchedulePage<T> page)
    {
        var items = page.Items.Take(Math.Max(1, page.Items.Length / 2)).ToImmutableArray();
        return page with { Items = items, NextOffset = page.Offset + items.Length < page.Total ? page.Offset + items.Length : null };
    }
    private void RegisterModuleScheduleTool(McpToolRegistry registry)
    {
        registry.Register(new("studio_module_schedule",
            "Read searchable, grouped discipline metadata schedules. Counts and straight pipe/wire/signal segment lengths describe retained layout roots, including hidden layers; they are not inferred connectivity or procurement quantities. CadModuleScheduleRequest supports query, class, group field/value, sort field/order and bounded scanning. Follow the three independent rows/groups/issues cursors with the same scheduleKey and query. Partial scan and unreadable metadata are explicit.",
            McpJson.Parse("""{"type":"object","properties":{"identity":{"type":"object","properties":{"documentId":{"type":"string"},"workspaceId":{"type":"string"},"viewVersion":{"type":"integer","minimum":0}},"required":["documentId","workspaceId","viewVersion"],"additionalProperties":false},"request":{"type":"object"}},"required":["identity","request"],"additionalProperties":false}"""), true), (args, token) =>
        {
            var identity = args.GetProperty("identity").Deserialize<StudioRequestIdentity>(McpJson.Options) ?? throw new ArgumentException("Missing identity.");
            var request = args.GetProperty("request").Deserialize<CadModuleScheduleRequest>(McpJson.Options) ?? throw new ArgumentException("Missing schedule request.");
            return ValueTask.FromResult(McpToolResult.FromObject(ExecuteRequest(identity, () => ModuleSchedule(request, registry.MaximumResultCharacters, token))));
        });
    }
}
