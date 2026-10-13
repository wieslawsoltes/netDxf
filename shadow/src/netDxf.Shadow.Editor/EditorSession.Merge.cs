namespace netDxf.Shadow.Editor;

public sealed partial class EditorSession
{
    /// <summary>Plans a branch merge without changing source state, selection or history.</summary>
    public ShadowMergePlan PreviewMerge(ShadowSnapshot ancestor, ShadowSnapshot incoming,
        ShadowMergeOptions? options = null, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            EnsureCurrent();
            return ShadowMerge.Plan(ancestor, store.Current, incoming, options, cancellationToken);
        }
    }

    /// <summary>Records a clean, exact-target merge as one canonical undo/redo command.</summary>
    public ShadowSnapshot ApplyMerge(ShadowMergePlan plan, string description = "Merge branch")
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (gate)
        {
            EnsureCurrent();
            if (plan.Patch is null) throw new ShadowConflictException("Resolve merge conflicts before applying the plan.");
            if (!ReferenceEquals(store.Current, plan.Target))
                throw new ShadowConflictException("Merge target changed or belongs to another branch. Replan the merge.");
            return Execute(description, plan.Patch);
        }
    }
}
