using netDxf.Entities;

namespace netDxf.Shadow.NetDxf;

/// <summary>Native entity removal unlinks hatch sources. Restore the proposed relationships
/// after owned payloads are populated and before entities are attached to their destination.</summary>
internal sealed class SourceHatchSourcesPlan
{
    private readonly Hatch hatch;
    private readonly bool beforeAssociative;
    private readonly EntityObject[][] beforeSources;
    private readonly bool afterAssociative;
    private readonly EntityObject[][] afterSources;
    private readonly bool hasTarget;

    internal SourceHatchSourcesPlan(Hatch hatch, ShadowReader proposed, Func<ObjectId, object> resolve)
    {
        this.hatch = hatch;
        beforeAssociative = hatch.Associative;
        beforeSources = hatch.BoundaryPaths.Select(path => path.Entities.ToArray()).ToArray();
        hasTarget = proposed.Object is not null;
        afterAssociative = proposed["Associative"].Boolean();
        afterSources = proposed["BoundaryPaths"].Items.Select(path => path["Entities"].Items
            .Select(entity => (EntityObject)resolve(entity.Id!.Value)).ToArray()).ToArray();
    }

    internal void Apply() { if (hasTarget) hatch.SetBoundarySources(afterAssociative, afterSources); }
    internal void Rollback() => hatch.SetBoundarySources(beforeAssociative, beforeSources);
}
