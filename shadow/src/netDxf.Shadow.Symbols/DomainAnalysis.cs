using System.Collections.Immutable;

namespace netDxf.Shadow.Symbols;

public sealed record DomainIssue(string Code,string Severity,string Message,ImmutableArray<ObjectId> Entities,Vec3? Position=null);
public sealed record DomainScan(ImmutableArray<DomainEntity> Records,ImmutableArray<DomainIssue> Issues,bool Complete,int ObjectsVisited,string? Limitation)
{
    /// <summary>Bounded visited roots available for relation checks, including unselected targets. Records retains the requested module/selection scope.</summary>
    public ImmutableArray<DomainEntity> ContextRecords { get; init; } = Records;
}

/// <summary>Analyzes only explicitly tagged layout roots and declared ports. Symbol strokes and geometric crossings never create network edges.</summary>
public static class DomainAnalysis
{
    public static DomainScan Analyze(ShadowSnapshot snapshot,string module,string layout="Model",double tolerance=.01,
        int maximumObjects=10000,int maximumIssues=500,IEnumerable<ObjectId>? selection=null,CancellationToken cancellationToken=default)
    {
        if (!double.IsFinite(tolerance) || tolerance is <=0 or >1e6 || maximumObjects is <1 or >100000 || maximumIssues is <1 or >10000) throw new ArgumentOutOfRangeException(nameof(tolerance));
        if (!snapshot.Layout(layout).Exists) throw new ArgumentException("Unknown layout.");
        var selected = selection?.ToHashSet(); if (selected?.Count==0) selected=null;
        var found = new List<DomainEntity>(); var issues = new List<DomainIssue>(); var visited=0; var complete=true; var issueOverflow=false;
        foreach(var entity in snapshot.DrawingEntities(layout))
        {
            cancellationToken.ThrowIfCancellationRequested(); if (++visited>maximumObjects) {visited--;complete=false;break;}
            try
            {
                var metadata=DomainMetadata.Read(entity); if(metadata is null) continue;
                if(entity.Id is not {} id) continue;
                found.Add(new(id,metadata,DomainMetadata.WorldPorts(snapshot,entity,metadata)));
            }
            catch(Exception error) when(error is FormatException or NotSupportedException or InvalidOperationException)
            { if(entity.Id is {} id) Issue("invalid-metadata","error",error.Message,[id]); }
        }
        var relevant=found.Where(e=>e.Data.Module==module && (selected is null || selected.Contains(e.Entity))).ToArray();
        var keys=found.GroupBy(e=>e.Data.Key,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        foreach(var duplicate in relevant.GroupBy(e=>e.Data.Tag,StringComparer.OrdinalIgnoreCase).Where(g=>!string.IsNullOrWhiteSpace(g.Key) && g.Select(e=>e.Data.Properties?.GetValueOrDefault("route")??e.Data.Key).Distinct().Count()>1))
            Issue("duplicate-tag","error","Tag '"+duplicate.Key+"' belongs to multiple components or routes.",duplicate.Select(e=>e.Entity).ToImmutableArray());
        foreach(var entity in relevant)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(entity.Data.Tag)) Issue("missing-tag","error","Domain object requires a tag.",[entity.Entity]);
            if(keys[entity.Data.Key].Length>1) Issue("duplicate-key","error","Copied domain identity is ambiguous; assign a fresh identity before connecting.",[entity.Entity]);
            var visible=snapshot.Read(entity.Entity)["Attributes"].Items.FirstOrDefault(a=>a["Tag"].Text.Equals("TAG",StringComparison.OrdinalIgnoreCase));
            if(visible.Exists && visible["Value"].Text!=entity.Data.Tag) Issue("tag-mismatch","error","Visible TAG and domain metadata differ; update them together.",[entity.Entity]);
            foreach(var reference in entity.Data.References.IsDefault?[]:entity.Data.References)
            {
                if(!keys.TryGetValue(reference.TargetKey,out var targets)) {if(complete)Issue("unresolved-reference","error","Referenced component is absent: "+reference.TargetKey,[entity.Entity]);continue;}
                if(targets.Length!=1) continue;
                var target=targets[0];var port=target.WorldPorts.FirstOrDefault(p=>p.Id==reference.TargetPortId);
                var source=entity.WorldPorts.FirstOrDefault(p=>p.Id==reference.PortId);
                if(port is null || source is null) {Issue("unknown-port","error","Reference names a port that is not declared.",[entity.Entity,target.Entity]);continue;}
                if (!(target.Data.References.IsDefault ? [] : target.Data.References).Any(r => r.TargetKey == entity.Data.Key && r.TargetPortId == reference.PortId && r.PortId == reference.TargetPortId && r.Role == reference.Role))
                    Issue("nonreciprocal-reference","warning","Target does not reference this port reciprocally.",[entity.Entity,target.Entity]);
                if(reference.Role!="connection") continue;
                if(source.Kind!=port.Kind) Issue("port-kind-mismatch","error","Connected port kinds differ.",[entity.Entity,target.Entity],source.Position);
                if(Vec3.Distance(source.Position,port.Position)>tolerance) Issue("connection-gap","warning","Referenced ports are physically separated.",[entity.Entity,target.Entity],source.Position);
                if(source.NominalSize is {Length:>0} a && port.NominalSize is {Length:>0} b && !a.Equals(b,StringComparison.OrdinalIgnoreCase)) Issue("nominal-size-mismatch","warning","Connected nominal sizes differ.",[entity.Entity,target.Entity],source.Position);
            }
            foreach(var port in entity.WorldPorts)
            {
                if((entity.Data.Properties?.GetValueOrDefault("openPorts")??"").Split(',').Contains(port.Id,StringComparer.OrdinalIgnoreCase)) continue;
                if((entity.Data.References.IsDefault?[]:entity.Data.References).Any(r=>r.PortId==port.Id)) continue;
                Issue("unassigned-port","info","Port '"+port.Id+"' has no explicit connection reference.",[entity.Entity],port.Position);
            }
        }
        return new(relevant.ToImmutableArray(),issues.ToImmutableArray(),complete&&!issueOverflow,visited,!complete?"Object scan limit reached; unresolved-reference checks are incomplete.":issueOverflow?"Issue limit reached.":null)
            { ContextRecords = found.ToImmutableArray() };
        void Issue(string code,string severity,string message,ImmutableArray<ObjectId> ids,Vec3? position=null)
        {if(issues.Count>=maximumIssues){issueOverflow=true;return;}issues.Add(new(code,severity,message,ids,position));}
    }
}
