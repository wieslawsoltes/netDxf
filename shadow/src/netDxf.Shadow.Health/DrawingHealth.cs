using System.Collections.Immutable;
using System.Globalization;

namespace netDxf.Shadow.Health;

public sealed record DrawingHealthOptions(double TinyLength = 1e-7, double GapTolerance = .01,
    int MaximumObjects = 250_000, long MaximumWork = 5_000_000, int MaximumIssues = 2_000,
    int MaximumValueDepth = 64, bool FindDuplicates = true, bool FindTinyGaps = true,
    bool FindUnusedResources = true);

/// <summary>Resource availability is supplied by the host; null means unknown, not missing.</summary>
public sealed record DrawingHealthResources(ImmutableHashSet<string>? AvailableShxFonts = null,
    ImmutableArray<ShadowDiagnostic> PresentationDiagnostics = default);
public sealed record DrawingHealthIssue(string Id, string Code, DiagnosticSeverity Severity, ObjectId? Object,
    string Path, string Message, ImmutableArray<ObjectId> RelatedObjects, Vec3? Position = null);
public sealed record DrawingHealthReport(Guid DocumentId, long Revision, bool Complete, long Work,
    ImmutableArray<DrawingHealthIssue> Issues, ImmutableArray<string> Coverage, ImmutableArray<string> Limitations);

/// <summary>Read-only, deterministic checks of immutable source data. No files are opened and no repairs are applied.</summary>
public static partial class DrawingHealth
{
    public static DrawingHealthReport Analyze(ShadowSnapshot snapshot, DrawingHealthOptions? options = null,
        DrawingHealthResources? resources = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); options ??= new(); resources ??= new();
        if (!double.IsFinite(options.TinyLength) || options.TinyLength < 0 || !double.IsFinite(options.GapTolerance)
            || options.GapTolerance <= options.TinyLength || options.MaximumObjects is < 1 or > 1_000_000
            || options.MaximumWork is < 1 or > 100_000_000 || options.MaximumIssues is < 1 or > 10_000
            || options.MaximumValueDepth is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(options));
        return new Analyzer(snapshot, options, resources, cancellationToken).Run();
    }

    private sealed partial class Analyzer(ShadowSnapshot snapshot, DrawingHealthOptions options,
        DrawingHealthResources resources, CancellationToken token)
    {
        private long work;
        private readonly List<DrawingHealthIssue> issues = [];
        private readonly HashSet<string> issueKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<ObjectId, HashSet<ObjectId>> users = [];
        private readonly Dictionary<ObjectId, ObjectId> owners = [];
        private readonly List<(ObjectId Entity, ObjectId Owner, Vec3 Point)> endpoints = [];
        private readonly Dictionary<string, ObjectId> duplicates = new(StringComparer.Ordinal);
        private bool complete = true;
        private sealed class BudgetReached : Exception { }
        private void Tick()
        {
            token.ThrowIfCancellationRequested();
            if (++work > options.MaximumWork) throw new BudgetReached();
        }
        private void Add(string code, DiagnosticSeverity severity, ObjectId? id, string path, string message,
            IEnumerable<ObjectId>? related = null, Vec3? position = null)
        {
            var refs = (related ?? []).Distinct().OrderBy(v => v.Value, StringComparer.Ordinal).ToImmutableArray();
            var key = code + ":" + id?.Value + ":" + path + ":" + string.Join(",", refs.Select(r => r.Value));
            if (!issueKeys.Add(key)) return;
            if (issues.Count >= options.MaximumIssues) throw new BudgetReached();
            issues.Add(new(key, code, severity, id, path, message, refs, position));
        }
        internal DrawingHealthReport Run()
        {
            try
            {
                Tick();
                if (snapshot.Objects.Count > options.MaximumObjects) throw new BudgetReached();
                if (!snapshot.Objects.ContainsKey(snapshot.Root)) Add("graph.root", DiagnosticSeverity.Error, snapshot.Root, "", "The document root is absent.");
                foreach (var node in snapshot.Objects.Values.OrderBy(n => n.Id.Value, StringComparer.Ordinal))
                {
                    Tick();
                    if (snapshot.EntityIds.Contains(node.Id) != node.Kind.HasFlag(ShadowObjectKind.Entity))
                        Add("graph.entity-index", DiagnosticSeverity.Error, node.Id, "", "Entity membership differs from the object's metadata.");
                    foreach (var property in node.Properties.OrderBy(p => p.Key, StringComparer.Ordinal)) Walk(node.Id, property.Value, property.Key, 0);
                    if (node.Kind.HasFlag(ShadowObjectKind.Block))
                        foreach (var entity in snapshot.Read(node.Id)["Entities"].Items)
                        {
                            Tick(); if (entity.Id is not { } id) continue;
                            if (owners.TryGetValue(id, out var owner) && owner != node.Id)
                                Add("graph.multiple-owners", DiagnosticSeverity.Error, id, "Owner", "The entity occurs in multiple block definitions.", [owner, node.Id]);
                            else owners[id] = node.Id;
                        }
                }
                foreach (var id in snapshot.EntityIds.OrderBy(i => i.Value, StringComparer.Ordinal))
                { Tick(); if (!snapshot.Objects.ContainsKey(id)) Add("graph.entity-index", DiagnosticSeverity.Error, id, "", "The entity index contains an absent object."); }
                foreach (var diagnostic in snapshot.Diagnostics.Concat(resources.PresentationDiagnostics.IsDefault ? [] : resources.PresentationDiagnostics)
                    .OrderBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Object?.Value, StringComparer.Ordinal))
                    Add("diagnostic." + diagnostic.Code, diagnostic.Severity, diagnostic.Object, "", diagnostic.Message);
                CheckGeometry(); CheckResources(); CheckBlockCycles();
                if (options.FindTinyGaps) CheckGaps();
            }
            catch (BudgetReached) { complete = false; }
            var coverage = new List<string> { "Graph references and entity ownership", "Finite numeric data and common geometry domains", "Recursive INSERT definitions", "Tiny LINE lengths" };
            if (options.FindDuplicates) coverage.Add("Exact same-owner LINE/CIRCLE/ARC/ELLIPSE/POINT geometry duplicates");
            if (options.FindTinyGaps) coverage.Add("LINE/ARC/open POLYLINE endpoint gaps in 3D");
            if (options.FindUnusedResources) coverage.Add("Unused block definitions, text/shape styles and linetypes");
            if (resources.AvailableShxFonts is not null) coverage.Add("SHX availability against supplied host inventory");
            return new(snapshot.DocumentId, snapshot.Revision, complete, work,
                issues.OrderBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.Object?.Value, StringComparer.Ordinal).ThenBy(i => i.Path, StringComparer.Ordinal)
                    .ThenBy(i => i.Id, StringComparer.Ordinal).ToImmutableArray(),
                coverage.ToImmutableArray(),
                ["Duplicate checks compare geometry, not appearance; they do not remove overlapping or near-duplicate objects.",
                 "Tiny gaps are endpoint proximity suggestions, not proof that entities should connect; blocks are analyzed in definition coordinates.",
                 "Spline intersections, self-intersections, hatch boundary validity and external-reference resolution are not checked.",
                 "Installed TrueType font availability is unknown. Missing SHX reports refer only to the supplied host inventory.",
                 "Health checks are not native source acceptance validation. A clean report does not guarantee that an edit can be applied."]);
        }
        private void Walk(ObjectId owner, ShadowValue value, string path, int depth)
        {
            Tick(); if (depth > options.MaximumValueDepth) throw new BudgetReached();
            switch (value)
            {
                case ShadowValue.Reference reference:
                    if (!snapshot.Objects.ContainsKey(reference.Id)) Add("graph.reference", DiagnosticSeverity.Error, owner, path, "Reference target is absent.", [reference.Id]);
                    if (!users.TryGetValue(reference.Id, out var set)) users[reference.Id] = set = [];
                    set.Add(owner); break;
                case ShadowValue.Fault fault: Add("capture.fault", DiagnosticSeverity.Error, owner, path, fault.Message); break;
                case ShadowValue.Atom atom when atom.TypeName is "System.Double" or "System.Single" or "Double" or "Single":
                    if (!double.TryParse(atom.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                        Add("geometry.nonfinite", DiagnosticSeverity.Error, owner, path, "Numeric data is not finite.");
                    break;
                case ShadowValue.Structure structure:
                    foreach (var field in structure.Fields.OrderBy(f => f.Key, StringComparer.Ordinal)) Walk(owner, field.Value, path + "." + field.Key, depth + 1); break;
                case ShadowValue.Sequence sequence:
                    for (var i = 0; i < sequence.Items.Length; i++) Walk(owner, sequence.Items[i], path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", depth + 1); break;
            }
        }
        private static bool Finite(Vec3 point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
        private static string N(double value) => (value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture);
        private static string V(Vec3 value) => N(value.X) + "," + N(value.Y) + "," + N(value.Z);
    }
}
