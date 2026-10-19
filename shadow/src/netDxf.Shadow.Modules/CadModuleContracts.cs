using System.Collections.Immutable;
using netDxf.Shadow.Drafting;

namespace netDxf.Shadow.Modules;

/// <summary>Declarative controls and discovery metadata; modules never receive a UI or HTTP host.</summary>
public sealed record CadModuleField(string Id, string Name, string Type = "text", bool Required = false,
    string Default = "", ImmutableArray<string> Choices = default, string Description = "");
public sealed record CadModuleCommand(string Id, string Name, string Description, int MinimumPoints = 0,
    int MaximumPoints = 0, bool RequiresSelection = false, bool RequiresSymbol = false,
    ImmutableArray<CadModuleField> Fields = default);
public sealed record CadModuleDescriptor(string Id, string Name, string Description, string Version,
    ImmutableArray<CadModuleCommand> Commands, ImmutableArray<string> Standards = default, int SchemaVersion = 1);
public sealed record CadModulePort(string Id, string Kind, Vec3 Position, Vec3 Direction = default,
    string? NominalSize = null);
public sealed record CadModuleSymbol(string SymbolId, string Version, string Name, string Description,
    string Category, string Units, Vec3 BasePoint, ImmutableArray<string> Standards,
    ImmutableArray<CadModuleField> Attributes, ImmutableArray<CadModulePort> Ports);
public sealed record CadModuleCatalog(int Offset, int Limit, int Total, ImmutableArray<CadModuleSymbol> Items);
public sealed record CadModuleRecord(ObjectId Entity, string Class, string Tag,
    ImmutableDictionary<string, string> Properties, ImmutableArray<CadModulePort> Ports);
public sealed record CadModuleIssue(string Code, string Severity, string Message,
    ImmutableArray<ObjectId> Entities, Vec3? Position = null);
public sealed record CadModuleAnalysis(Guid DocumentId, long Revision, string ModuleId,
    ImmutableArray<CadModuleRecord> Records, ImmutableArray<CadModuleIssue> Issues,
    bool Complete = true, int ObjectsVisited = 0, string? Limitation = null);
public sealed record CadModuleAnalysisOptions(double ConnectionTolerance = .01, int MaximumObjects = 10000,
    int MaximumIssues = 500, ImmutableArray<ObjectId> Entities = default);
public sealed record CadModuleCommandRequest(string Command, ImmutableArray<Vec3> Points = default,
    ImmutableArray<ObjectId> Entities = default, string? SymbolId = null, string? SymbolVersion = null,
    ImmutableDictionary<string, string>? Properties = null);

/// <summary>Pure planning leaves source mutation, history and workspace isolation with the host.</summary>
public interface ICadModule
{
    CadModuleDescriptor Descriptor { get; }
    CadModuleCatalog Catalog(string query = "", int offset = 0, int limit = 50);
    CadModuleAnalysis Analyze(ShadowSnapshot snapshot, string layout = "Model",
        CadModuleAnalysisOptions? options = null, CancellationToken cancellationToken = default);
    DraftingPlan Plan(ShadowSnapshot snapshot, CadModuleCommandRequest request, string layout = "Model",
        CancellationToken cancellationToken = default);
}
