using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Units;
using netDxf.Shadow;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;
using netDxf.Shadow.Symbols;

internal static class DomainScheduleVerification
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.CadState(), StudioJson.CreateOptions()).GetProperty("state");
    private static StudioRequestIdentity Identity(Studio studio)
    { var state = State(studio); return new(state.GetProperty("documentId").GetGuid(), state.GetProperty("workspaceId").GetString()!, state.GetProperty("viewVersion").GetInt64()); }
    private static MemoryStream Save(DxfDocument document, bool binary = false)
    { var stream = new MemoryStream(); Require(document.Save(stream, binary), "Schedule fixture saves."); stream.Position = 0; return stream; }
    private static void Malformed(EntityObject entity)
    {
        var data = new XData(new ApplicationRegistry(DomainMetadata.ApplicationName));
        data.XDataRecord.Add(new(XDataCode.String, "unsupported-version"));
        entity.XData.Add(data);
    }
    private static DxfDocument Fixture()
    {
        var document = new DxfDocument(); document.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
        var hidden = new Layer("Hidden schedule roots") { IsVisible = false, IsFrozen = true }; document.Layers.Add(hidden);
        var a = new Line(Vector3.Zero, new Vector3(3, 4, 0));
        var b = new Line(Vector3.Zero, new Vector3(0, 0, 12)) { Layer = hidden };
        document.Entities.Add(a); document.Entities.Add(b);
        foreach (var (line, key, tag, service) in new[] { (a, "pipe-a", "P-20", "Cooling"), (b, "pipe-b", "P-10", "cooling") })
            DomainMetadata.Write(line, new("pid", "pipe", key, tag,
                Properties: ImmutableDictionary<string, string>.Empty.Add("service", service).Add("route", "R1").Add("size", "DN50")));
        var block = new Block("Schedule symbol"); block.Record.Units = DrawingUnits.Millimeters;
        var nested = new Line(Vector2.Zero, Vector2.UnitX); block.Entities.Add(nested);
        DomainMetadata.Write(nested, new("pid", "pipe", "nested-stroke", "Not a layout root"));
        var insert = new Insert(block, new Vector3(10, 10, 0)) { Scale = new(2), Rotation = 90 }; document.Entities.Add(insert);
        DomainMetadata.Write(insert, new("pid", "pump", "pump-a", "Z-01", "schedule.pump", "1.2.3",
            Ports: [new("out", "process", Vec3.UnitX, Vec3.UnitX)]));
        var foreign = new Line(new Vector2(30, 0), new Vector2(40, 0)); document.Entities.Add(foreign);
        DomainMetadata.Write(foreign, new("electrical", "wire", "wire-a", "W-01"));
        document.Entities.Add(new netDxf.Entities.Point(new Vector3(99, 99, 0)));
        return document;
    }

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Add(string name, Action run) => tests.Add(("discipline-schedule/" + name, run));
        Add("group search ordering hidden roots world ports and native units", () =>
        {
            var document = Fixture(); var source = new ShadowSession(document); var before = source.Current;
            var result = DomainSchedule.Build(before, "pid", options: new(GroupBy: "service"));
            Require(result.Complete && result.ScanComplete && result.ObjectsVisited == 5 && result.RecordsScanned == 3 && result.Rows.Length == 3,
                "Schedule scans layout roots including hidden/frozen layers, without nested definition strokes or foreign modules.");
            Require(result.DrawingUnits == "Millimeters", "Drawing units are named, not converted to presumed procurement units.");
            Require(result.Rows.Select(r => r.Tag).SequenceEqual(["P-10", "P-20", "Z-01"]), "Default ordering is deterministic by tag.");
            var cooling = result.Groups.Single(g => g.Value == "Cooling");
            Require(cooling.Objects == 2 && cooling.Routes == 1 && cooling.MeasuredSegments == 2 && cooling.SegmentLength == 17,
                "Case-insensitive property groups count one explicit route and sum 3D source segment lengths.");
            var port = result.Rows.Single(r => r.Class == "pump").Ports.Single();
            Require(Vec3.Distance(port.Position, new(10, 12)) < 1e-7 && Vec3.Distance(port.Direction, Vec3.UnitY) < 1e-7,
                "Schedule symbol ports use native insertion position, rotation, scale and units.");
            Require(DomainSchedule.Build(before, "pid", options: new(Query: "dn50", Class: "PIPE")).Rows.Length == 2,
                "Search and class filters are case-insensitive.");
            Require(DomainSchedule.Build(before, "pid", options: new(Query: "route")).Rows.Length == 2, "Property names are searchable.");
            Require(DomainSchedule.Build(before, "pid", options: new(Query: "schedule.pump")).Rows.Single().Key == "pump-a",
                "Symbol identifiers are searchable.");
            Require(DomainSchedule.Build(before, "pid", options: new(GroupBy: "SERVICE", GroupValue: "COOLING")).Rows.Length == 2,
                "Group drilldown matches its field with the same case-insensitive semantics.");
            Require(DomainSchedule.Build(before, "pid", options: new(Descending: true)).Rows.Select(r => r.Tag)
                .SequenceEqual(["Z-01", "P-20", "P-10"]), "Descending ordering is deterministic.");
            Require(ReferenceEquals(before, source.Current) && document.Layers["Hidden schedule roots"].IsFrozen,
                "Schedule reads do not mutate the snapshot or reveal hidden roots.");
        });
        Add("native text and binary imports preserve metadata schedules and straight lengths", () =>
        {
            var document = Fixture();
            foreach (var binary in new[] { false, true })
            {
                using var bytes = Save(document, binary); var loaded = DxfDocument.Load(bytes)!;
                var result = DomainSchedule.Build(new ShadowSession(loaded).Current, "pid", options: new(GroupBy: "service"));
                Require(result.Complete && result.Rows.Length == 3 && result.Groups.Single(g => g.Value == "Cooling").SegmentLength == 17,
                    "Native DXF roundtrip preserves hidden metadata roots and exact source lengths.");
                Require(result.Rows.All(row => row.Entity.Value.StartsWith("H:", StringComparison.Ordinal)) &&
                    result.Rows.Single(row => row.Key == "pump-a").SymbolVersion == "1.2.3", "Source handles and explicit versions survive import.");
            }
        });
        Add("scan bounds invalid metadata issue caps cancellation and request validation are truthful", () =>
        {
            var document = Fixture();
            for (var i = 0; i < 105; i++)
            {
                var line = new Line(new Vector2(i, 100), new Vector2(i, 101)); document.Entities.Add(line); Malformed(line);
            }
            var snapshot = new ShadowSession(document).Current;
            var limited = DomainSchedule.Build(snapshot, "pid", options: new(MaximumObjects: 1));
            Require(!limited.ScanComplete && !limited.Complete && limited.ObjectsVisited == 1 && limited.Rows.Length == 1 &&
                limited.Limitations.Any(value => value.Contains("scan limit", StringComparison.OrdinalIgnoreCase)), "Bounded scans do not claim complete schedules.");
            var all = DomainSchedule.Build(snapshot, "pid", options: new(MaximumObjects: 110));
            Require(all.ScanComplete && !all.Complete && all.InvalidRecords == 105 && all.Issues.Length == 100 &&
                all.RecordsScanned == 3 && all.Limitations.Any(value => value.Contains("105", StringComparison.Ordinal)),
                "Unreadable roots are counted independently of the capped issue stream and cannot imply known module membership.");
            Throws<OperationCanceledException>(() => DomainSchedule.Build(snapshot, "pid", token: new(true)));
            foreach (var options in new DomainScheduleOptions[] { new(Query: null!), new(Query: new('x', 257)), new(Class: null!),
                         new(GroupBy: "bad\nfield"), new(SortBy: ""), new(MaximumObjects: 0), new(MaximumObjects: 100001) })
                Throws<ArgumentException>(() => DomainSchedule.Build(snapshot, "pid", options: options));
            Throws<ArgumentException>(() => DomainSchedule.Build(snapshot, "pid", "Missing layout"));
        });
        Add("nonfinite transformed symbol ports become explicit invalid records rather than unbounded JSON", () =>
        {
            foreach (var scale in new[] { 1e13, 1e308 })
            {
                var document = new DxfDocument(); var block = new Block("Extreme scale");
                block.Entities.Add(new Line(Vector2.Zero, Vector2.UnitX));
                var insert = new Insert(block, Vector3.Zero) { Scale = new(scale) }; document.Entities.Add(insert);
                DomainMetadata.Write(insert, new("pid", "pump", "extreme", "Extreme", Ports: [new("out", "process", new(2, 0, 0))]));
                var result = DomainSchedule.Build(new ShadowSession(document).Current, "pid");
                Require(!result.Complete && result.InvalidRecords == 1 && result.Issues.Length == 1 && result.Rows.IsEmpty,
                    "Transformed ports outside finite supported bounds must be diagnosed before transport serialization.");
                _ = JsonSerializer.Serialize(result, McpJson.Options);
            }
        });
        Add("Studio continuations bind query snapshot layout workspace view and module configuration", () =>
        {
            using var studio = new Studio(false); using var bytes = Save(Fixture()); studio.Load(bytes);
            studio.CadSetModule(new(studio.CadRevision, "pid", true));
            var request = new CadModuleScheduleRequest(studio.CadRevision, "pid", Limit: 1);
            var first = studio.CadModuleSchedule(request); var before = State(studio);
            var next = request with { ScheduleKey = first.ScheduleKey, RowOffset = first.Rows.NextOffset!.Value };
            Require(studio.CadModuleSchedule(next).Rows.Items.Single().Entity != first.Rows.Items.Single().Entity, "A continuation advances actual row offsets.");
            Throws<ArgumentException>(() => studio.CadModuleSchedule(next with { ScheduleKey = null }));
            Throws<ArgumentException>(() => studio.CadModuleSchedule(next with { RowOffset = 100 }));
            Throws<ShadowConflictException>(() => studio.CadModuleSchedule(next with { Options = new(Query: "changed") }));
            Throws<OperationCanceledException>(() => studio.CadModuleSchedule(request, new(true)));
            Throws<ArgumentException>(() => studio.CadModuleSchedule(request with { Options = new(GroupBy: "\n") }));
            Require(studio.CadModuleSchedule(next).ScheduleKey == first.ScheduleKey, "Cancelled or invalid requests do not evict a valid query receipt.");
            Require(State(studio).GetProperty("undoCount").GetInt32() == before.GetProperty("undoCount").GetInt32() &&
                studio.CadRevision == before.GetProperty("revision").GetInt64(), "Schedule paging leaves source/history untouched.");
            studio.SetLayout(new(studio.CadRevision, "Layout1"));
            Throws<ShadowConflictException>(() => studio.CadModuleSchedule(next));
            studio.SetLayout(new(studio.CadRevision, "Model"));
            first = studio.CadModuleSchedule(request); next = request with { ScheduleKey = first.ScheduleKey };
            studio.CadSetModule(new(studio.CadRevision, "pid", false));
            Throws<InvalidOperationException>(() => studio.CadModuleSchedule(next));
            studio.CadSetModule(new(studio.CadRevision, "pid", true));
            Throws<ShadowConflictException>(() => studio.CadModuleSchedule(next));
            first = studio.CadModuleSchedule(request); next = request with { ScheduleKey = first.ScheduleKey };
            studio.Fork(new(studio.CadRevision, "Schedule review"));
            Throws<ShadowConflictException>(() => studio.CadModuleSchedule(next));
            studio.SwitchWorkspace(new(studio.CadRevision, "live"));
            Throws<ShadowConflictException>(() => studio.CadModuleSchedule(next));
            first = studio.CadModuleSchedule(request); next = request with { ScheduleKey = first.ScheduleKey };
            studio.CadCommand(new(studio.CadRevision, new("LINE", [new(800, 800), new(810, 800)])));
            Throws<ShadowConflictException>(() => studio.CadModuleSchedule(next with { Revision = studio.CadRevision }));
        });
        Add("MCP adaptive rows groups and issues preserve all data and reject foreign identities", () =>
        {
            var document = new DxfDocument();
            for (var i = 0; i < 80; i++)
            {
                var line = new Line(new Vector2(i, 0), new Vector2(i, 1)); document.Entities.Add(line);
                var properties = Enumerable.Range(0, 6).ToImmutableDictionary(n => "detail" + n, n => new string((char)('a' + n), 1000))
                    .Add("route", "route-" + i);
                DomainMetadata.Write(line, new("pid", "pipe", "schedule-" + i, "P-" + i, Properties: properties));
            }
            for (var i = 0; i < 3; i++)
            { var line = new Line(new Vector2(i, 10), new Vector2(i, 11)); document.Entities.Add(line); Malformed(line); }
            using var bytes = Save(document); using var studio = new Studio(false); studio.Load(bytes);
            studio.CadSetModule(new(studio.CadRevision, "pid", true));
            var registry = studio.CreateMcpRegistry(); var identity = Identity(studio); var before = State(studio);
            Require(registry.Tools.Single(tool => tool.Name == "studio_module_schedule").ReadOnly, "Schedule MCP tool is read-only.");
            var request = new CadModuleScheduleRequest(studio.CadRevision, "pid", new(GroupBy: "route"), Limit: 200);
            var rows = new HashSet<ObjectId>(); var groups = new HashSet<string>(); var issues = new HashSet<ObjectId>(); var passes = 0; var shrunk = false;
            while (true)
            {
                var answer = registry.ExecuteAsync("studio_module_schedule", McpJson.Element(new { identity, request })).AsTask().GetAwaiter().GetResult();
                Require(!answer.IsError && answer.Text.Length <= registry.MaximumResultCharacters, "Each adaptive MCP page fits its real registry budget.");
                var page = JsonSerializer.Deserialize<CadModuleScheduleResult>(answer.Text, McpJson.Options)!;
                Require(!page.Complete && page.ScanComplete && page.InvalidRecords == 3 && page.RecordsScanned == 80, "Metadata failures remain explicit on every page.");
                if (passes == 0) shrunk = page.Rows.Items.Length < page.Rows.Total;
                foreach (var row in page.Rows.Items) Require(rows.Add(row.Entity), "No duplicated schedule row.");
                foreach (var group in page.Groups.Items) Require(groups.Add(group.Value), "No duplicated schedule group.");
                foreach (var issue in page.Issues.Items) Require(issues.Add(issue.Entity), "No duplicated schedule issue.");
                if (page.Rows.NextOffset is null && page.Groups.NextOffset is null && page.Issues.NextOffset is null) break;
                request = request with { ScheduleKey = page.ScheduleKey, RowOffset = page.Rows.NextOffset ?? page.Rows.Total,
                    GroupOffset = page.Groups.NextOffset ?? page.Groups.Total, IssueOffset = page.Issues.NextOffset ?? page.Issues.Total };
                Require(++passes < 100, "Adaptive paging must terminate.");
            }
            Require(shrunk && rows.Count == 80 && groups.Count == 80 && issues.Count == 3, "All three streams retain complete coverage despite shortened pages.");
            foreach (var wrong in new[] { identity with { DocumentId = Guid.NewGuid() }, identity with { WorkspaceId = "other" }, identity with { ViewVersion = identity.ViewVersion + 1 } })
                Require(registry.ExecuteAsync("studio_module_schedule", McpJson.Element(new { identity = wrong, request })).AsTask().GetAwaiter().GetResult().IsError,
                    "Foreign document/workspace/view identities are refused.");
            Require(State(studio).GetProperty("undoCount").GetInt32() == before.GetProperty("undoCount").GetInt32() &&
                studio.CadRevision == before.GetProperty("revision").GetInt64(), "MCP schedule paging is read-only.");
        });
    }
}
