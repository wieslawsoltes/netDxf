using System.Collections.Immutable;
using netDxf.Shadow.Drafting;
using static netDxf.Shadow.Symbols.SymbolAuthoring;

namespace netDxf.Shadow.Symbols;

public static class DomainPlans
{
    public static DraftingPlan Route(ShadowSnapshot snapshot, string module, string kind, IReadOnlyList<Vec3> points,
        string tag, IReadOnlyDictionary<string,string>? properties = null, string layout = "Model", CancellationToken cancellationToken = default)
    {
        if (points.Count is < 2 or > 256 || points.Any(p => !p.IsFinite || p.Length > 1e12)) throw new ArgumentException("Routes require 2–256 finite points.");
        for (var i=1;i<points.Count;i++) if (Vec3.Distance(points[i-1],points[i]) < 1e-9) throw new ArgumentException("Route segments must have nonzero length.");
        var owner = snapshot.Layout(layout)["AssociatedBlock"].Id ?? throw new ArgumentException("Unknown layout.");
        var layer = properties?.GetValueOrDefault("layer") ?? "0";
        var layerValue = snapshot.Document["Layers"].Items.FirstOrDefault(l => l["Name"].Text == layer);
        if (!layerValue.Exists || layerValue["IsLocked"].Boolean() || layerValue["IsFrozen"].Boolean()) throw new ArgumentException("Choose an existing unlocked, thawed route layer.");
        var group = Guid.NewGuid().ToString("N");
        var keys = Enumerable.Range(1, points.Count-1).Select(i => group + "." + i).ToArray();
        var edit = new ShadowGraphEdit(snapshot); var created = ImmutableArray.CreateBuilder<ObjectId>();
        for (var i=1;i<points.Count;i++) created.Add(edit.AddEntity(owner, "netDxf.Entities.Line", P(("StartPoint", V(points[i-1])), ("EndPoint", V(points[i])), ("Layer", layerValue.Value))));
        var working = edit.Build().Apply(snapshot);
        for (var i=0;i<created.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var refs = ImmutableArray.CreateBuilder<DomainReference>();
            if (i>0) refs.Add(new("start", keys[i-1], "end"));
            if (i+1<created.Count) refs.Add(new("end", keys[i+1], "start"));
            var values = (properties ?? ImmutableDictionary<string,string>.Empty).ToImmutableDictionary(StringComparer.OrdinalIgnoreCase).SetItem("route", group).SetItem("segment", (i+1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            var direction = (points[i+1]-points[i]).Normalized;
            var data = new DomainRecord(module, kind, keys[i], tag, Properties:values,
                Ports:[new("start", kind == "wire" ? "electrical" : kind == "signal" ? "signal" : "process", points[i], -direction, values.GetValueOrDefault("nominalSize")),
                    new("end", kind == "wire" ? "electrical" : kind == "signal" ? "signal" : "process", points[i+1], direction, values.GetValueOrDefault("nominalSize"))], References:refs.ToImmutable());
            working = Attach(working, created[i], data).Patch.Apply(working);
        }
        return new("Route " + kind, ShadowGraphPatch.Between(snapshot, working), created.ToImmutable());
    }
    public static DraftingPlan Update(ShadowSnapshot snapshot, IEnumerable<ObjectId> entities, IReadOnlyDictionary<string,string> properties, CancellationToken cancellationToken = default)
    {
        var ids = entities.Distinct().Take(1001).ToArray(); if (ids.Length is <1 or >1000) throw new ArgumentException("Select 1–1000 domain objects.");
        var working = snapshot;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = DraftingGuard.Entity(working,id); var data = DomainMetadata.Read(entity) ?? throw new ArgumentException("Selection contains an object without domain metadata.");
            var values = (data.Properties ?? ImmutableDictionary<string,string>.Empty).ToBuilder();
            foreach (var pair in properties) if (pair.Key != "tag") values[pair.Key] = pair.Value;
            var tag = properties.GetValueOrDefault("tag", data.Tag);
            working = Attach(working,id,data with { Tag=tag, Properties=values.ToImmutable(), Ports=SymbolNative.Ports(data.Ports, values) }).Patch.Apply(working);
            var edit = new ShadowGraphEdit(working);
            foreach (var attr in working.Read(id)["Attributes"].Items)
                if (attr.Id is { } attributeId && (attr["Tag"].Text.Equals("TAG",StringComparison.OrdinalIgnoreCase) || values.ContainsKey(attr["Tag"].Text)))
                    edit.Set(attributeId,"Value",S(attr["Tag"].Text.Equals("TAG",StringComparison.OrdinalIgnoreCase) ? tag : values[attr["Tag"].Text]));
            working = edit.Build().Apply(working);
        }
        return new("Update domain data",ShadowGraphPatch.Between(snapshot,working),[]);
    }
    public static DraftingPlan Connect(ShadowSnapshot snapshot,ObjectId source,ObjectId target,string sourcePort,string targetPort,string role="connection")
    {
        if (source == target || role is not ("connection" or "association")) throw new ArgumentException("Choose two different objects and a supported reference role.");
        var a = DomainMetadata.Read(DraftingGuard.Entity(snapshot,source)) ?? throw new ArgumentException("Source has no domain data.");
        var b = DomainMetadata.Read(DraftingGuard.Entity(snapshot,target)) ?? throw new ArgumentException("Target has no domain data.");
        var pa = a.Ports.FirstOrDefault(p=>p.Id==sourcePort) ?? throw new ArgumentException("Unknown source port.");
        var pb = b.Ports.FirstOrDefault(p=>p.Id==targetPort) ?? throw new ArgumentException("Unknown target port.");
        if (role=="connection" && pa.Kind!=pb.Kind) throw new ArgumentException("Physical connections require compatible port kinds.");
        var ar = (a.References.IsDefault ? [] : a.References).Add(new(sourcePort,b.Key,targetPort,role)).Distinct().ToImmutableArray();
        var br = (b.References.IsDefault ? [] : b.References).Add(new(targetPort,a.Key,sourcePort,role)).Distinct().ToImmutableArray();
        var working = Attach(snapshot,source,a with {References=ar}).Patch.Apply(snapshot);
        working = Attach(working,target,b with {References=br}).Patch.Apply(working);
        return new("Connect domain ports",ShadowGraphPatch.Between(snapshot,working),[]);
    }
}
