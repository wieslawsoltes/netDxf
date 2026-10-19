using netDxf.Shadow.Disciplines;
using netDxf.Shadow.Modules;
using static netDxf.Shadow.Disciplines.DomainCommands;
using static netDxf.Shadow.Disciplines.DomainValidation;

namespace netDxf.Shadow.Electrical;

public sealed class ElectricalModule() : DomainCadModule(ElectricalSymbols.Catalog, "wire")
{
    public override CadModuleDescriptor Descriptor { get; } = new("electrical", "Electrical", "Schematic devices, circuit-tagged wires and explicit terminal connectivity.", "1.0.0",
        Create("Route wire",
            [Field("tag", "Wire designation", required: true), Field("circuit", "Circuit", required: true, value: "C1"), Field("crossSection", "Conductor area (mm²)", "number", true, "1.5"), Field("voltage", "Rated voltage", "number", value: "230")],
            [Field("tag", "Reference designation"), Field("circuit", "Circuit"), Field("crossSection", "Conductor area (mm²)", "number"), Field("voltage", "Rated voltage", "number"), Field("openPorts", "Intentional open terminals (comma separated)")]),
        ["IEC 60617 reference; original schematic artwork"]);
    protected override void Validate(DomainValidation context)
    {
        foreach (var entity in context.Records)
        {
            context.Require(entity, "circuit", "missing-circuit", "Electrical object requires a circuit identifier.");
            if (entity.Data.Class == "wire" && !Positive(Value(entity, "crossSection"))) context.Add("invalid-conductor-area", "Wire requires a positive conductor cross-section in mm².", entity);
            if (Value(entity, "voltage") is { Length: > 0 } voltage && !Positive(voltage)) context.Add("invalid-voltage", "Rated voltage must be positive when specified.", entity);
        }
        foreach (var (source, _, target) in context.Connections())
            if (Value(source, "circuit") is { Length: > 0 } circuit && Value(target, "circuit") is { Length: > 0 } other && !circuit.Equals(other, StringComparison.OrdinalIgnoreCase))
                context.Add("circuit-mismatch", "Connected electrical terminals belong to different circuits.", source, "error", target);
        context.ConsistentRoutes("circuit", "crossSection", "voltage");
    }
}
