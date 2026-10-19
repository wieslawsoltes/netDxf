using netDxf.Shadow.Disciplines;
using netDxf.Shadow.Modules;
using static netDxf.Shadow.Disciplines.DomainCommands;
using static netDxf.Shadow.Disciplines.DomainValidation;

namespace netDxf.Shadow.Pid;

public sealed class PidModule() : DomainCadModule(PidSymbols.Catalog, "pipe")
{
    public override CadModuleDescriptor Descriptor { get; } = new("pid", "P&ID", "Tagged process equipment, pipes and PCE requests with explicit port relations.", "1.0.0",
        Create("Route process pipe",
            [Field("tag", "Line tag", required: true), Field("nominalSize", "Nominal size", "number", true, "50"), Field("service", "Service", value: "Process"),
                Field("spec", "Specification", value: "CS"), Field("flow", "Flow direction", "select", true, "forward", "forward", "reverse", "bidirectional")],
            [Field("tag", "Tag"), Field("service", "Service"), Field("nominalSize", "Nominal size", "number"), Field("spec", "Specification"),
                Field("flow", "Flow direction", "select", choices: ["", "forward", "reverse", "bidirectional"]), Field("FUNCTION", "PCE function"), Field("LOOP", "PCE loop"), Field("openPorts", "Intentional open ports (comma separated)")]),
        ["EN ISO 10628-2", "EN 62424"]);
    protected override void Validate(DomainValidation context)
    {
        foreach (var entity in context.Records)
        {
            if (entity.Data.Class == "pipe")
            {
                if (!Positive(Value(entity, "nominalSize"))) context.Add("invalid-nominal-size", "Process pipe requires a positive nominal size.", entity);
                context.Require(entity, "service", "missing-service", "Process pipe requires a service.");
                if (Value(entity, "flow") is not ("forward" or "reverse" or "bidirectional")) context.Add("missing-flow", "Process pipe requires a declared forward, reverse or bidirectional flow.", entity);
            }
            if (entity.Data.Class == "instrument")
            {
                var function = Value(entity, "FUNCTION"); var loop = Value(entity, "LOOP");
                if (function.Length is < 1 or > 8 || function.Any(c => c is < 'A' or > 'Z')) context.Add("invalid-pce-function", "PCE function must contain 1–8 uppercase letters.", entity);
                if (loop.Length is < 1 or > 32 || loop.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))) context.Add("invalid-pce-loop", "PCE request requires a bounded alphanumeric loop designation.", entity);
                if (entity.Data.References.IsDefaultOrEmpty) context.Add("unassociated-instrument", "PCE request has no explicit measured-object or signal reference.", entity, "warning");
            }
        }
        foreach (var (source, reference, target) in context.Connections())
        {
            if (source.Data.Class != "pipe" || target.Data.SymbolId is not ("pid.pump.liquid" or "pid.valve.check")) continue;
            var flow = Value(source, "flow"); if (flow is not ("forward" or "reverse")) continue;
            var flowsToTarget = reference.PortId == (flow == "forward" ? "end" : "start");
            if (reference.TargetPortId is "inlet" or "outlet" && flowsToTarget != (reference.TargetPortId == "inlet"))
                context.Add("inconsistent-flow", "Pipe flow opposes the pump or check-valve inlet/outlet direction.", source, "error", target);
        }
        context.ConsistentRoutes("nominalSize", "service", "spec", "flow");
    }
}
