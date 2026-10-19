using netDxf.Shadow.Modules;

namespace netDxf.Shadow.Studio;

public sealed record CadModuleToggleRequest(long Revision, string Module, bool Enabled);
public sealed record CadModuleCatalogRequest(string Module, string Query = "", int Offset = 0, int Limit = 50);
public sealed record CadModuleAnalysisRequest(long Revision, string Module, CadModuleAnalysisOptions? Options = null);
public sealed record CadModuleExecuteRequest(long Revision, string Module, CadModuleCommandRequest Command);

public sealed partial class Studio
{
    private readonly CadModuleRegistry cadModules;
    private long moduleConfigurationVersion;
    // Built-in module composition is kept at the application layer, outside the registry.
    private static IEnumerable<ICadModule> DefaultCadModules() =>
        [new netDxf.Shadow.Pid.PidModule(), new netDxf.Shadow.Electrical.ElectricalModule(), new netDxf.Shadow.Piping.PipingModule()];

    public object CadModules()
    {
        lock (gate) return new { configurationVersion = moduleConfigurationVersion, modules = cadModules.States };
    }
    public object CadSetModule(CadModuleToggleRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            var previous = cadModules.States.FirstOrDefault(m => m.Descriptor.Id == request.Module)
                ?? throw new ArgumentException("Unknown CAD module.");
            if (previous.Enabled != request.Enabled)
            {
                cadModules.SetEnabled(request.Module, request.Enabled);
                moduleConfigurationVersion++;
                PublishChange();
            }
            return CadState();
        }
    }
    public object CadModuleSymbols(CadModuleCatalogRequest request)
    {
        lock (gate) return cadModules.Catalog(request.Module, request.Query, request.Offset, request.Limit);
    }
    public object CadAnalyzeModule(CadModuleAnalysisRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision);
            return cadModules.Analyze(request.Module, Current, layout, request.Options, token);
        }
    }
    public object CadRunModule(CadModuleExecuteRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            Check(request.Revision); ArgumentNullException.ThrowIfNull(request.Command);
            if (activeBranch?.BlockRoot is not null)
                throw new InvalidOperationException("Finish block editing before creating or changing discipline objects.");
            var selected = CadSelected(Scene());
            var command = request.Command with { Entities = AuthoringSelection(request.Command.Entities, selected) };
            return ApplyAuthoring(cadModules.Plan(request.Module, Current, command, layout, token), selected, "module", request with { Command = command });
        }
    }
}
