using System.Collections.Immutable;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Modules;

var checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
var module = new FixtureModule(); var registry = new CadModuleRegistry([module]);
var snapshot = new ShadowSnapshot(Guid.NewGuid(), 0, new("root"), ImmutableDictionary<ObjectId, ShadowObject>.Empty, [], []);
Check(registry.States.Single().Enabled, "Enabled registration");
Check(System.Text.Json.JsonSerializer.Serialize(registry.States).Contains("Fixture"), "Optional arrays serialize safely in module discovery");
Check(registry.Catalog("fixture").Total == 1, "Catalog dispatch");
Check(registry.Analyze("fixture", snapshot).Complete, "Default options normalize");
registry.SetEnabled("fixture", false);
Check(!registry.States.Single().Enabled && !registry.IsEnabled("fixture"), "Disabled metadata remains discoverable");
Throws<InvalidOperationException>(() => registry.Catalog("fixture"));
Throws<InvalidOperationException>(() => registry.Analyze("fixture", snapshot));
Throws<InvalidOperationException>(() => registry.Plan("fixture", snapshot, new("create", [new(1, 2, 0)])));
Check(snapshot.Revision == 0 && snapshot.Objects.Count == 0, "Disabling preserves data");
registry.SetEnabled("fixture", true);
Check(registry.Catalog("fixture").Total == 1, "Reenable restores contributions");
Throws<ArgumentException>(() => new CadModuleRegistry([module, module]));
Throws<ArgumentException>(() => registry.SetEnabled("missing", true));
Throws<ArgumentException>(() => registry.Catalog("fixture", limit: 201));
Throws<ArgumentException>(() => registry.Catalog("fixture", offset: -1));
Throws<ArgumentException>(() => registry.Catalog("fixture", query: new string('a', 513)));
Throws<ArgumentException>(() => registry.Analyze("fixture", snapshot, options: new(ConnectionTolerance: double.NaN)));
Throws<ArgumentException>(() => registry.Analyze("fixture", snapshot, options: new(MaximumObjects: 100001)));
Throws<ArgumentException>(() => registry.Plan("fixture", snapshot, new("missing")));
Throws<ArgumentException>(() => registry.Plan("fixture", snapshot, new("create")));
Throws<ArgumentException>(() => registry.Plan("fixture", snapshot, new("create", [new(double.NaN, 0, 0)])));
Throws<ArgumentException>(() => registry.Plan("fixture", snapshot, new("create", [new(0, 0, 0)], Properties: ImmutableDictionary<string,string>.Empty.Add("tag", ""))));
Throws<ShadowConflictException>(() => registry.Plan("fixture", snapshot, new("create", [new(0, 0, 0)], [new("missing")])));
Check(registry.Plan("fixture", snapshot, new("create", [new(0, 0, 0)])).Patch.Changes.IsEmpty, "Plan is nonmutating");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
Throws<OperationCanceledException>(() => registry.Analyze("fixture", snapshot, token: cancelled.Token));
Throws<OperationCanceledException>(() => registry.Plan("fixture", snapshot, new("create", [new(0, 0, 0)]), token: cancelled.Token));
Console.WriteLine($"PASS: {checks} CAD module registry checks.");

sealed class FixtureModule : ICadModule
{
    public CadModuleDescriptor Descriptor => new("fixture", "Fixture", "Contract verification", "1.0.0", [new("create", "Create", "Create a fixture", 1, 1, Fields: [new("tag", "Tag", Required: true, Default: "F-1")])]);
    public CadModuleCatalog Catalog(string query = "", int offset = 0, int limit = 50) => new(offset, limit, 1, []);
    public CadModuleAnalysis Analyze(ShadowSnapshot snapshot, string layout = "Model", CadModuleAnalysisOptions? options = null, CancellationToken cancellationToken = default) => new(snapshot.DocumentId, snapshot.Revision, "fixture", [], []);
    public DraftingPlan Plan(ShadowSnapshot snapshot, CadModuleCommandRequest request, string layout = "Model", CancellationToken cancellationToken = default) => new("Fixture", ShadowGraphPatch.Between(snapshot, snapshot), []);
}
