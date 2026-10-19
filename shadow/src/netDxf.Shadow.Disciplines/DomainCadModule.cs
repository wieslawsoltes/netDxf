using System.Collections.Immutable;
using System.Globalization;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Modules;
using netDxf.Shadow.Symbols;

namespace netDxf.Shadow.Disciplines;

/// <summary>Shared module adapter; each discipline owns its catalog, command fields and semantic rules.</summary>
public abstract class DomainCadModule(SymbolCatalog symbols, string routeClass) : ICadModule
{
    public abstract CadModuleDescriptor Descriptor { get; }
    public CadModuleCatalog Catalog(string query = "", int offset = 0, int limit = 50)
    {
        var page = symbols.Search(query, offset, limit);
        return new(page.Offset, page.Limit, page.Total, page.Items.Select(s => new CadModuleSymbol(s.Id, s.Version, s.Name,
            s.Description, s.Category, s.Units, s.BasePoint, s.Standards,
            s.Attributes.Select(a => new CadModuleField(a.Id, a.Name, Required: a.Required, Default: a.Default, Choices: [])).ToImmutableArray(),
            s.Ports.Select(Port).ToImmutableArray())).ToImmutableArray());
    }
    public CadModuleAnalysis Analyze(ShadowSnapshot snapshot, string layout = "Model", CadModuleAnalysisOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        var scan = DomainAnalysis.Analyze(snapshot, Descriptor.Id, layout, options.ConnectionTolerance, options.MaximumObjects,
            options.MaximumIssues, options.Entities.IsDefault ? null : options.Entities, cancellationToken);
        var context = new DomainValidation(scan, options.MaximumIssues, cancellationToken);
        foreach (var entity in scan.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entity.Data.SymbolId is { } id)
                try { symbols.Resolve(id, entity.Data.SymbolVersion); }
                catch (ArgumentException) { context.Add("unknown-symbol-version", "Symbol version is absent from this module catalog.", entity); }
        }
        Validate(context);
        return new(snapshot.DocumentId, snapshot.Revision, Descriptor.Id,
            scan.Records.Select(e => new CadModuleRecord(e.Entity, e.Data.Class, e.Data.Tag,
                (e.Data.Properties ?? ImmutableDictionary<string, string>.Empty).SetItem("key", e.Data.Key)
                    .SetItem("symbolId", e.Data.SymbolId ?? "").SetItem("symbolVersion", e.Data.SymbolVersion ?? ""),
                e.WorldPorts.Select(Port).ToImmutableArray())).ToImmutableArray(), context.Issues,
            scan.Complete && !context.Truncated, scan.ObjectsVisited, scan.Limitation ?? (context.Truncated ? "Discipline issue limit reached." : null));
    }
    protected abstract void Validate(DomainValidation context);
    public DraftingPlan Plan(ShadowSnapshot snapshot, CadModuleCommandRequest request, string layout = "Model", CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = request with { Points = request.Points.IsDefault ? [] : request.Points, Entities = request.Entities.IsDefault ? [] : request.Entities };
        var properties = (request.Properties ?? ImmutableDictionary<string, string>.Empty).WithComparers(StringComparer.OrdinalIgnoreCase);
        string Get(string name, string fallback = "") => properties.GetValueOrDefault(name, fallback);
        double Number(string name, double fallback) => properties.TryGetValue(name, out var value) && value.Length > 0 ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
        switch (request.Command)
        {
            case "place":
                if (request.Points.Length != 1) throw new ArgumentException("Choose one insertion point.");
                return SymbolAuthoring.Place(snapshot, symbols.Resolve(request.SymbolId ?? "", request.SymbolVersion), request.Points[0],
                    Descriptor.Id, Get("tag"), properties, layout, Number("rotation", 0), Number("scale", 1), Get("layer", "0"), cancellationToken);
            case "route":
                return DomainPlans.Route(snapshot, Descriptor.Id, routeClass, request.Points, Get("tag"), properties, layout, cancellationToken);
            case "update":
                foreach (var id in request.Entities)
                    if (DomainMetadata.Read(snapshot.Read(id))?.Module != Descriptor.Id) throw new ArgumentException("Selected objects must belong to this discipline.");
                return DomainPlans.Update(snapshot, request.Entities, properties.Where(p => p.Value.Length > 0).ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), cancellationToken);
            case "connect":
                if (request.Entities.Length != 2) throw new ArgumentException("Select exactly two domain objects.");
                return DomainPlans.Connect(snapshot, request.Entities[0], request.Entities[1], Get("sourcePort"), Get("targetPort"), Get("role", "connection"));
            default: throw new ArgumentException("Unknown module command.");
        }
    }
    private static CadModulePort Port(SymbolPort p) => new(p.Id, p.Kind, p.Position, p.Direction, p.NominalSize);
}

/// <summary>Bounded issue sink and reusable domain rules for extension modules.</summary>
public sealed class DomainValidation(DomainScan scan, int maximumIssues, CancellationToken cancellationToken)
{
    private readonly List<CadModuleIssue> issues = scan.Issues.Select(i => new CadModuleIssue(i.Code, i.Severity, i.Message, i.Entities, i.Position)).ToList();
    public ImmutableArray<DomainEntity> Records => scan.Records;
    public ImmutableArray<CadModuleIssue> Issues => issues.ToImmutableArray();
    public bool Truncated { get; private set; }
    public void Add(string code, string message, DomainEntity entity, string severity = "error", DomainEntity? related = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (issues.Count >= maximumIssues) { Truncated = true; return; }
        issues.Add(new(code, severity, message, related is null ? [entity.Entity] : [entity.Entity, related.Entity]));
    }
    public static string Value(DomainEntity entity, string name) => entity.Data.Properties?.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value ?? "";
    public static bool Positive(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0;
    public IEnumerable<(DomainEntity Source, DomainReference Reference, DomainEntity Target)> Connections()
    {
        var context = scan.ContextRecords.IsDefault ? Records : scan.ContextRecords;
        var keys = context.GroupBy(e => e.Data.Key).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
        foreach (var entity in Records)
            foreach (var reference in entity.Data.References.IsDefault ? [] : entity.Data.References)
                if (reference.Role == "connection" && keys.TryGetValue(reference.TargetKey, out var target))
                    yield return (entity, reference, target);
    }
    public void Require(DomainEntity entity, string property, string code, string message)
    { if (string.IsNullOrWhiteSpace(Value(entity, property))) Add(code, message, entity); }
    public void ConsistentRoutes(params string[] properties)
    {
        var context = scan.ContextRecords.IsDefault ? Records : scan.ContextRecords;
        var routes = context.Where(e => Value(e, "route").Length > 0).ToLookup(e => (e.Data.Module, Route: Value(e, "route")));
        foreach (var group in Records.Where(e => Value(e, "route").Length > 0).GroupBy(e => (e.Data.Module, Route: Value(e, "route"))))
            foreach (var property in properties)
                if (routes[group.Key].Select(e => Value(e, property)).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                    Add("inconsistent-route-" + property.ToLowerInvariant(), "Segments of a route have inconsistent " + property + ".", group.First());
    }
}
