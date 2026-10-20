using System.Collections.Immutable;
using netDxf.Entities;
using netDxf.Shadow.Pid;
using netDxf.Shadow.Symbols;
using netDxf.Units;

namespace netDxf.Shadow.Samples;

/// <summary>Attaches an explicit demonstration network; legend lines, flow-arrow strokes and symbol geometry are never treated as pipes.</summary>
internal static class ProcessDiagramMetadata
{
    internal static void Attach(DxfDocument document, IReadOnlyList<Line> pipes, IReadOnlyList<Line> signals)
    {
        var records = new List<(EntityObject Entity, DomainRecord Data, ImmutableArray<SymbolPort> World)>();
        foreach (var insert in document.Entities.Inserts)
        {
            var tag = insert.Attributes.AttributeWithTag("TAG").Value;
            var symbol = PidSymbols.Catalog.Resolve(insert.Block.Name switch
            {
                "PID_StorageTank_ConicalRoof" => "pid.tank.storage", "PID_LiquidPump" => "pid.pump.liquid",
                "PID_ManualValve" => "pid.valve.manual", "PID_CheckValve" => "pid.valve.check", _ => "pid.instrument.pce"
            });
            insert.Block.Record.Units = DrawingUnits.Millimeters;
            DomainMetadata.Write(insert.Block, new("symbols", "definition", symbol.Id + "@" + symbol.Version, symbol.Name,
                symbol.Id, symbol.Version, Properties: ImmutableDictionary<string,string>.Empty.Add("units", symbol.Units).Add("standards", string.Join("; ", symbol.Standards)), Ports: symbol.Ports) { Passages = symbol.Passages });
            var properties = insert.Attributes.ToImmutableDictionary(a => a.Tag, a => a.Value, StringComparer.OrdinalIgnoreCase)
                .SetItem("service", "Water").SetItem("spec", "CS");
            var ports = symbol.Ports.Select(p => p with { NominalSize = p.Kind == "signal" ? null :
                tag == "T-101" ? p.Id is "vent" or "drain" ? "25" : "50" : tag == "P-101" ? p.Id == "inlet" ? "50" : "40" :
                tag == "V-104" ? "25" : tag is "V-103" or "NRV-101" ? "40" : "50" }).ToImmutableArray();
            var data = new DomainRecord("pid", symbol.Category, "pid.sample." + tag, tag, symbol.Id, symbol.Version, properties, ports, []) { Passages = symbol.Passages };
            records.Add((insert, data, ports.Select(p => p with { Position = p.Position + V(insert.Position) }).ToImmutableArray()));
        }
        for (var index = 0; index < pipes.Count; index++)
        {
            var group = index < 2 ? "SUPPLY" : index < 4 ? "P-101-SUCTION" : index < 7 ? "P-101-DISCHARGE" : index < 9 ? "VENT" : "DRAIN";
            var size = index < 4 ? "50" : index < 7 ? "40" : "25";
            var ports = Endpoints(pipes[index], "process", size);
            if (index == 4) ports = ports.Add(new("pressure", "signal", new(216,120), Vec3.UnitY));
            if (index == 6) ports = ports.Add(new("flow", "signal", new(318,120), Vec3.UnitY));
            var properties = ImmutableDictionary<string,string>.Empty.Add("route", group).Add("nominalSize", size).Add("service", "Water").Add("spec", "CS").Add("flow", "forward");
            if (index is 0 or 6 or 8 or 11) properties = properties.Add("openPorts", index == 0 ? "start" : "end");
            var data = new DomainRecord("pid", "pipe", "pid.sample.pipe." + index, group, Properties: properties, Ports: ports, References: []) { Passages = [new("start", "end", "forward")] };
            records.Add((pipes[index], data, ports));
        }
        for (var index = 0; index < signals.Count; index++)
        {
            var tag = index < 2 ? "LI-101-SIGNAL" : index == 2 ? "PI-102-SIGNAL" : "FI-103-SIGNAL";
            var ports = Endpoints(signals[index], "signal", null);
            records.Add((signals[index], new("pid", "signal", "pid.sample.signal." + index, tag,
                Properties: ImmutableDictionary<string,string>.Empty.Add("route",tag), Ports: ports, References: []) { Passages = [new("start", "end")] }, ports));
        }
        // Only authored ports are matched. Crossings and lines within block definitions cannot create edges.
        foreach (var item in records)
        {
            var references = item.World.SelectMany(port => records.Where(other => other.Data.Key != item.Data.Key)
                .SelectMany(other => other.World.Where(target => target.Kind == port.Kind && Vec3.Distance(target.Position, port.Position) < 1e-8)
                    .Select(target => new DomainReference(port.Id, other.Data.Key, target.Id)))).ToImmutableArray();
            DomainMetadata.Write(item.Entity, item.Data with { References = references });
        }
    }
    private static ImmutableArray<SymbolPort> Endpoints(Line line, string kind, string? size)
    {
        var delta = V(line.EndPoint - line.StartPoint).Normalized;
        return [new("start", kind, V(line.StartPoint), -delta, size), new("end", kind, V(line.EndPoint), delta, size)];
    }
    private static Vec3 V(Vector3 p) => new(p.X,p.Y,p.Z);
}
