using System.Collections.Immutable;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;

namespace netDxf.Shadow.Studio;

public sealed record ForkRequest(long Revision, string Name);
public sealed record WorkspaceRequest(long Revision, string Id);
public sealed record BranchDeleteRequest(long Revision, string Id, long BranchRevision);
public sealed record MergePreviewRequest(Guid DocumentId, string BranchId, long TargetRevision,
    long IncomingRevision, string Choice = "Unresolved");
public sealed record MergeApplyRequest(Guid DocumentId, string PlanId, long TargetRevision, long IncomingRevision);
public sealed record StudioRequestIdentity(Guid DocumentId, string WorkspaceId, long ViewVersion);

public sealed partial class Studio
{
    private const int MaximumBranches = 8;
    private readonly Dictionary<string, BranchWorkspace> branches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingMerge> mergePlans = new(StringComparer.Ordinal);
    private BranchWorkspace? activeBranch;
    private long viewVersion;
    private EditorSession editor => activeBranch?.Editor ?? liveEditor;
    private ShadowSnapshot Current => editor.Snapshot;
    private string WorkspaceId => activeBranch?.Id ?? "live";
    private string layout { get => activeBranch?.Layout ?? liveLayout; set { if (activeBranch is { } branch) branch.Layout = value; else liveLayout = value; } }
    private ShxFontSet CurrentFonts => activeBranch?.Fonts ?? shxFonts.Current;
    private SortedDictionary<long, ShadowSnapshot> retained => activeBranch?.Retained ?? liveRetained;
    private Dictionary<long, RenderCompiler> retainedCompilers => activeBranch?.RetainedCompilers ?? liveRetainedCompilers;

    private sealed class BranchWorkspace(string id, string name, ShadowSnapshot ancestor, ShxFontSet fonts,
        CadSceneBuilder sceneBuilder, RenderCompiler compiler, string layout)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public ShadowSnapshot Ancestor { get; private set; } = ancestor;
        public EditorSession Editor { get; private set; } = new(new ShadowMemoryStore(ancestor));
        public ShxFontSet Fonts { get; } = fonts;
        public CadSceneBuilder SceneBuilder { get; } = sceneBuilder;
        public RenderCompiler Compiler { get; } = compiler;
        public string Layout { get; set; } = layout;
        public CadLinetypeAlignment LinetypeAlignment { get; set; }
        public SortedDictionary<long, ShadowSnapshot> Retained { get; } = new();
        public Dictionary<long, RenderCompiler> RetainedCompilers { get; } = new();

        // Rebase only after successful source adoption. The source may canonicalize identities,
        // values and ownership; the speculative preview must never become its replacement.
        public void Rebase(ShadowSnapshot canonical)
        {
            Ancestor = canonical;
            Editor = new(new ShadowMemoryStore(canonical));
            Retained.Clear(); RetainedCompilers.Clear();
            if (!canonical.Layout(Layout).Exists) Layout = "Model";
        }
    }
    private sealed record PendingMerge(BranchWorkspace Branch, ShadowSnapshot Incoming, ShadowMergePlan Plan,
        RenderCompiler Compiler, ShxFontSet Fonts);

    /// <summary>
    /// HTTP mutation identity is checked under the same gate as the entire synchronous operation.
    /// Revision numbers alone do not distinguish documents, isolated branches or a switch away/back.
    /// </summary>
    public T ExecuteRequest<T>(StudioRequestIdentity identity, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (identity.DocumentId != Current.DocumentId || identity.WorkspaceId != WorkspaceId || identity.ViewVersion != viewVersion)
                throw new ShadowConflictException("The document or workspace changed. Refresh before editing.");
            return operation();
        }
    }

    private (CadSceneBuilder Builder, RenderCompiler Compiler) FrozenServices(ShxFontSet fonts)
    {
        var shx = new ShxEntityProjector(fonts);
        var builder = new CadSceneBuilder([new DimensionEntityProjector(resources: [shx]), shx]);
        return (builder, new(builder, textEngine));
    }

    public object Fork(ForkRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); RequireLive();
            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 80 || name.Any(char.IsControl))
                throw new ArgumentException("Branch names require 1–80 printable characters.");
            if (branches.Count >= MaximumBranches) throw new InvalidOperationException("The sample retains at most eight branches. Discard a branch first.");
            if (branches.Values.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("A branch with that name already exists.");
            var id = Guid.NewGuid().ToString("N"); var fonts = shxFonts.Current;
            var services = FrozenServices(fonts);
            activeBranch = new(id, name, session.Current, fonts, services.Builder, services.Compiler, layout) { LinetypeAlignment = PatternAlignment };
            branches.Add(id, activeBranch); viewVersion = checked(viewVersion + 1); graph = null;
            return UpdatedState();
        }
    }

    public object SwitchWorkspace(WorkspaceRequest request)
    {
        lock (gate)
        {
            Check(request.Revision);
            var next = request.Id == "live" ? null : FindBranch(request.Id);
            if (ReferenceEquals(next, activeBranch)) return State();
            activeBranch = next; viewVersion = checked(viewVersion + 1); graph = null;
            return UpdatedState();
        }
    }

    public object DiscardBranch(BranchDeleteRequest request)
    {
        lock (gate)
        {
            Check(request.Revision); var branch = FindBranch(request.Id);
            if (branch.Editor.Snapshot.Revision != request.BranchRevision) throw new ShadowConflictException("The branch changed before deletion.");
            branches.Remove(branch.Id); RemovePlans(branch);
            if (ReferenceEquals(activeBranch, branch)) { activeBranch = null; viewVersion = checked(viewVersion + 1); graph = null; }
            return UpdatedState();
        }
    }

    public object PreviewMerge(MergePreviewRequest request, CancellationToken token = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var branch = FindBranch(request.BranchId);
            CheckMergeInputs(request.DocumentId, branch, request.TargetRevision, request.IncomingRevision);
            if (!Enum.TryParse<ShadowMergeChoice>(request.Choice, false, out var choice) || !Enum.IsDefined(choice))
                throw new ArgumentException("Choose Unresolved, Target or Incoming conflict policy.");
            var incoming = branch.Editor.Snapshot;
            var plan = liveEditor.PreviewMerge(branch.Ancestor, incoming, new(ConflictChoice: choice), token);
            RemovePlans(branch);
            string? id = null;
            if (plan.CanApply)
            {
                id = Guid.NewGuid().ToString("N");
                mergePlans.Add(id, new(branch, incoming, plan, FrozenServices(shxFonts.Current).Compiler, shxFonts.Current));
            }
            var edits = plan.Patch?.Changes ?? [];
            return new
            {
                documentId = Current.DocumentId, branchId = branch.Id, planId = id, plan.CanApply,
                targetRevision = plan.Target.Revision, incomingRevision = incoming.Revision,
                ancestorRevision = branch.Ancestor.Revision, choice = choice.ToString(), plan.Work,
                added = edits.Count(e => e.Before is null), removed = edits.Count(e => e.After is null),
                changed = edits.Count(e => e.Before is not null && e.After is not null),
                conflictCount = plan.Conflicts.Length,
                // Bound HTTP diagnostic output independently of the core's planning budget.
                conflicts = plan.Conflicts.Take(200).Select(c => new
                {
                    id = c.Object.Value, path = c.Path, kind = c.Kind.ToString(), resolution = c.Resolution.ToString(),
                    ancestor = Summarize(c.BaseValue), target = Summarize(c.TargetValue), incoming = Summarize(c.IncomingValue),
                    referencedObject = c.ReferencedObject?.Value
                }).ToArray()
            };
        }
    }

    public object ApplyMerge(MergeApplyRequest request)
    {
        lock (gate)
        {
            var pending = FindPlan(request.PlanId);
            CheckMergeInputs(request.DocumentId, pending.Branch, request.TargetRevision, request.IncomingRevision);
            ValidatePlan(pending);
            liveEditor.ApplyMerge(pending.Plan, "Merge " + pending.Branch.Name);
            pending.Branch.Rebase(session.Current);
            RemovePlans(pending.Branch);
            activeBranch = null; viewVersion = checked(viewVersion + 1); graph = null;
            return UpdatedState();
        }
    }

    private void RequireLive()
    {
        if (activeBranch is not null) throw new InvalidOperationException("Return to the live document for this operation. Branch changes must be merged before saving DXF.");
    }
    private BranchWorkspace FindBranch(string id) => id is not null && branches.TryGetValue(id, out var branch)
        ? branch : throw new ShadowConflictException("The branch is unavailable or belongs to a replaced document.");
    private PendingMerge FindPlan(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return id is not null && mergePlans.TryGetValue(id, out var pending) ? pending
            : throw new ShadowConflictException("The merge preview expired. Recompute it before applying.");
    }
    private void CheckMergeInputs(Guid documentId, BranchWorkspace branch, long targetRevision, long incomingRevision)
    {
        if (session.Current.DocumentId != documentId || session.Current.Revision != targetRevision || branch.Editor.Snapshot.Revision != incomingRevision)
            throw new ShadowConflictException("The live document or incoming branch changed. Recompute the merge preview.");
    }
    private void ValidatePlan(PendingMerge pending)
    {
        if (!ReferenceEquals(pending.Plan.Target, session.Current) || !ReferenceEquals(pending.Incoming, pending.Branch.Editor.Snapshot)
            || !ReferenceEquals(pending.Fonts, shxFonts.Current))
            throw new ShadowConflictException("The merge inputs changed. Recompute the preview.");
    }
    private void RemovePlans(BranchWorkspace branch)
    {
        foreach (var key in mergePlans.Where(p => ReferenceEquals(p.Value.Branch, branch)).Select(p => p.Key).ToArray()) mergePlans.Remove(key);
    }
    private static object Summarize(ShadowValue? value) => value switch
    {
        null => new { kind = "Absent", text = "(absent)" },
        ShadowValue.Atom a => new { kind = a.TypeName, text = a.Text.Length <= 512 ? a.Text : a.Text[..512] + "…" },
        ShadowValue.Reference r => new { kind = "Reference", text = r.Id.Value },
        ShadowValue.Structure s => new { kind = s.TypeName, text = $"{s.Fields.Count} fields" },
        ShadowValue.Sequence s => new { kind = "Sequence", text = $"{s.Items.Length} ordered items" },
        ShadowValue.Fault => new { kind = "Fault", text = "Capture fault" },
        _ => new { kind = "Null", text = "null" }
    };
}
