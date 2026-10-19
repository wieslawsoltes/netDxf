using System.Collections.Immutable;
using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Modules;

public sealed record CadModuleState(CadModuleDescriptor Descriptor, bool Enabled);

/// <summary>A host-owned registry. Disabling contributions never deletes drawing data.</summary>
public sealed class CadModuleRegistry
{
    private readonly object gate = new();
    private readonly ImmutableDictionary<string, ICadModule> modules;
    private readonly HashSet<string> disabled = new(StringComparer.Ordinal);
    public CadModuleRegistry(IEnumerable<ICadModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var builder = ImmutableDictionary.CreateBuilder<string, ICadModule>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            var descriptor = module.Descriptor;
            ValidateId(descriptor.Id);
            if (descriptor.SchemaVersion != 1 || string.IsNullOrWhiteSpace(descriptor.Name) ||
                string.IsNullOrWhiteSpace(descriptor.Version) || descriptor.Commands.IsDefault)
                throw new ArgumentException("Module metadata must have a name, version and schema version 1.");
            var commands = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in descriptor.Commands)
            {
                ValidateId(command.Id);
                if (!commands.Add(command.Id) || command.MinimumPoints < 0 || command.MaximumPoints < command.MinimumPoints || command.MaximumPoints > 10000)
                    throw new ArgumentException("Module commands must be unique and declare valid point limits.");
            }
            if (!builder.TryAdd(descriptor.Id, module)) throw new ArgumentException($"Duplicate module '{descriptor.Id}'.");
        }
        this.modules = builder.ToImmutable();
    }
    public ImmutableArray<CadModuleState> States
    {
        get { lock (gate) return modules.Values.OrderBy(m => m.Descriptor.Id, StringComparer.Ordinal)
            .Select(m => new CadModuleState(Normalize(m.Descriptor), !disabled.Contains(m.Descriptor.Id))).ToImmutableArray(); }
    }
    public void SetEnabled(string id, bool enabled)
    {
        lock (gate)
        {
            _ = Find(id);
            if (enabled) disabled.Remove(id); else disabled.Add(id);
        }
    }
    public bool IsEnabled(string id)
    {
        lock (gate) { _ = Find(id); return !disabled.Contains(id); }
    }
    public CadModuleCatalog Catalog(string id, string query = "", int offset = 0, int limit = 50)
    {
        if (query is null || query.Length > 512 || offset < 0 || limit is < 1 or > 200)
            throw new ArgumentException("Catalog search requires at most 512 characters and a page size of 1–200.");
        var page = Enabled(id).Catalog(query, offset, limit);
        return page with { Items = page.Items.IsDefault ? [] : page.Items.Select(symbol => symbol with {
            Standards = symbol.Standards.IsDefault ? [] : symbol.Standards,
            Attributes = Fields(symbol.Attributes), Ports = symbol.Ports.IsDefault ? [] : symbol.Ports
        }).ToImmutableArray() };
    }
    public CadModuleAnalysis Analyze(string id, ShadowSnapshot snapshot, string layout = "Model",
        CadModuleAnalysisOptions? options = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        options ??= new();
        if (!double.IsFinite(options.ConnectionTolerance) || options.ConnectionTolerance is <= 0 or > 1000000 ||
            options.MaximumObjects is < 1 or > 100000 || options.MaximumIssues is < 1 or > 5000 ||
            (!options.Entities.IsDefault && options.Entities.Length > 10000))
            throw new ArgumentException("Invalid module analysis tolerance or work budget.");
        token.ThrowIfCancellationRequested();
        return Enabled(id).Analyze(snapshot, layout, options, token);
    }
    public DraftingPlan Plan(string id, ShadowSnapshot snapshot, CadModuleCommandRequest request,
        string layout = "Model", CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(request);
        var module = Enabled(id);
        var command = module.Descriptor.Commands.FirstOrDefault(c => c.Id == request.Command)
            ?? throw new ArgumentException("Unknown module command.");
        var points = request.Points.IsDefault ? ImmutableArray<Vec3>.Empty : request.Points;
        var entities = request.Entities.IsDefault ? ImmutableArray<ObjectId>.Empty : request.Entities;
        if (points.Length < command.MinimumPoints || points.Length > command.MaximumPoints ||
            points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || Math.Abs(p.X) > 1e12 || Math.Abs(p.Y) > 1e12 || Math.Abs(p.Z) > 1e12))
            throw new ArgumentException($"'{command.Name}' requires {command.MinimumPoints}–{command.MaximumPoints} finite points.");
        if (entities.Length > 10000 || (command.RequiresSelection && entities.IsEmpty) || entities.Distinct().Count() != entities.Length)
            throw new ArgumentException("Module selection must be unique and meet the command's selection requirement.");
        if (entities.Any(id => !snapshot.Objects.ContainsKey(id))) throw new ShadowConflictException("A module selection object no longer exists.");
        if (command.RequiresSymbol && (string.IsNullOrWhiteSpace(request.SymbolId) || string.IsNullOrWhiteSpace(request.SymbolVersion)))
            throw new ArgumentException("Choose an explicit symbol identifier and version.");
        if (request.SymbolId?.Length > 256 || request.SymbolVersion?.Length > 64 || request.Properties?.Count > 128 ||
            request.Properties?.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 128 || p.Value is null || p.Value.Length > 8192) == true)
            throw new ArgumentException("Module properties exceed the request limits.");
        var properties = (request.Properties ?? ImmutableDictionary<string, string>.Empty).ToBuilder();
        foreach (var field in command.Fields.IsDefault ? [] : command.Fields)
        {
            var value = request.Properties?.GetValueOrDefault(field.Id) ?? field.Default;
            if (field.Required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"'{field.Name}' is required.");
            if (!field.Choices.IsDefaultOrEmpty && !field.Choices.Contains(value, StringComparer.Ordinal))
                throw new ArgumentException($"'{field.Name}' must be one of the advertised choices.");
            properties.TryAdd(field.Id, value);
        }
        token.ThrowIfCancellationRequested();
        return module.Plan(snapshot, request with { Points = points, Entities = entities, Properties = properties.ToImmutable() }, layout, token);
    }
    private ICadModule Enabled(string id)
    {
        lock (gate) { var module = Find(id); if (disabled.Contains(id)) throw new InvalidOperationException($"Module '{id}' is disabled."); return module; }
    }
    private ICadModule Find(string id) => id is not null && modules.TryGetValue(id, out var module)
        ? module : throw new ArgumentException("Unknown CAD module.", nameof(id));
    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-'))
            throw new ArgumentException("Module and command identifiers use 1–64 lowercase ASCII letters, digits or hyphens.");
    }
    private static ImmutableArray<CadModuleField> Fields(ImmutableArray<CadModuleField> fields) => fields.IsDefault ? []
        : fields.Select(field => field with { Choices = field.Choices.IsDefault ? [] : field.Choices }).ToImmutableArray();
    private static CadModuleDescriptor Normalize(CadModuleDescriptor descriptor) => descriptor with {
        Standards = descriptor.Standards.IsDefault ? [] : descriptor.Standards,
        Commands = descriptor.Commands.Select(command => command with { Fields = Fields(command.Fields) }).ToImmutableArray()
    };
}
