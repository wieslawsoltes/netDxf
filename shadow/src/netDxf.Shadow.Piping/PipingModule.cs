using netDxf.Shadow.Disciplines;
using netDxf.Shadow.Modules;
using static netDxf.Shadow.Disciplines.DomainCommands;
using static netDxf.Shadow.Disciplines.DomainValidation;

namespace netDxf.Shadow.Piping;

public sealed class PipingModule() : DomainCadModule(PipingSymbols.Catalog, "pipe")
{
    public override CadModuleDescriptor Descriptor { get; } = new("piping", "Piping", "Pipe runs, versioned fittings, nominal sizes and specification continuity.", "1.0.0",
        Create("Route pipe",
            [Field("tag", "Line tag", required: true), Field("nominalSize", "Nominal size", "number", true, "50"), Field("service", "Service", value: "Water"), Field("spec", "Specification", required: true, value: "CS")],
            [Field("tag", "Tag"), Field("nominalSize", "Nominal size", "number"), Field("DN", "Fitting nominal size", "number"), Field("inletSize", "Reducer inlet size", "number"),
                Field("outletSize", "Reducer outlet size", "number"), Field("service", "Service"), Field("spec", "Specification"), Field("openPorts", "Intentional open ports (comma separated)")]),
        ["Schematic piping convention"]);
    protected override void Validate(DomainValidation context)
    {
        foreach (var entity in context.Records)
        {
            if (entity.Data.Class is not ("pipe" or "fitting")) continue;
            var size = Value(entity, "nominalSize"); if (size.Length == 0) size = Value(entity, "DN");
            if (!Positive(size)) context.Add("invalid-nominal-size", "Pipe or fitting requires a positive nominal size.", entity);
            context.Require(entity, "spec", "missing-specification", "Pipe or fitting requires a specification.");
            if (entity.Data.SymbolId == "piping.reducer")
            {
                var inlet = Value(entity, "inletSize"); var outlet = Value(entity, "outletSize");
                if (!Positive(inlet) || !Positive(outlet) ||
                    double.Parse(inlet, System.Globalization.CultureInfo.InvariantCulture) == double.Parse(outlet, System.Globalization.CultureInfo.InvariantCulture))
                    context.Add("invalid-reducer-sizes", "Reducer requires different positive inlet and outlet sizes.", entity);
            }
        }
        foreach (var (source, _, target) in context.Connections())
            if (Value(source, "spec") is { Length: > 0 } specification && Value(target, "spec") is { Length: > 0 } other && !specification.Equals(other, StringComparison.OrdinalIgnoreCase))
                context.Add("specification-mismatch", "Connected piping objects use different specifications.", source, "warning", target);
        context.ConsistentRoutes("nominalSize", "spec", "service");
    }
}
