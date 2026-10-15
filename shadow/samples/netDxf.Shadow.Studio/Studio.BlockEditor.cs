using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Studio;

public sealed record CadBlockCreateRequest(long Revision, string Name, Vec3 BasePoint,
    string Disposition = "Convert", string Layer = "0", string Description = "");
public sealed record CadBlockRenameRequest(long Revision, string Block, string Name);
public sealed record CadBlockBeginRequest(long Revision, string Block);
public sealed record CadBlockFinishRequest(long Revision, long TargetRevision, bool Save, bool Discard = false);

public sealed partial class Studio
{
    public object CadCreateBlock(CadBlockCreateRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision);
            if (!Enum.TryParse<BlockSourceDisposition>(request.Disposition, false, out var mode) || !Enum.IsDefined(mode))
                throw new ArgumentException("Choose Convert, Retain or Remove source objects.");
            var selected = CadSelected(Scene());
            var input = new BlockCreation(request.Name, request.BasePoint, selected, mode, request.Layer, request.Description);
            var plan = activeBranch?.BlockRoot is { } root ? drafting.CreateBlockInDefinition(Current, input, root, token) :
                drafting.CreateBlock(Current, input, layout, token);
            plan.Execute(editor); cadSelectionOrder = plan.Created;
            editor.Select(DraftingQueries.Expand(Scene(), plan.Created)); PublishChange(); return CadState();
        }
    }

    public object CadRenameBlock(CadBlockRenameRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision);
            DraftingBlocks.Rename(Current, new(request.Block), request.Name).Execute(editor);
            PublishChange(); return CadState();
        }
    }

    /// <summary>A definition editor is a normal immutable branch with an explicit scene root.
    /// It does not steal entity ownership or temporarily rewrite live INSERT transforms.</summary>
    public object CadBeginBlock(CadBlockBeginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision); RequireLive();
            var root = new ObjectId(request.Block); var block = DraftingBlocks.Definition(Current, root);
            if (branches.Count >= MaximumBranches) throw new InvalidOperationException("Discard an existing branch before opening a block editor.");
            var id = Guid.NewGuid().ToString("N"); var fonts = shxFonts.Current; var services = FrozenServices(fonts);
            var branch = new BranchWorkspace(id, "Block: " + block["Name"].Text, session.Current, fonts,
                services.Builder, services.Compiler, layout)
                { BlockRoot = root, LinetypeAlignment = PatternAlignment, Annotation = Annotation };
            // Validate the definition view before publishing the workspace transition.
            branch.Compiler.Compile(branch.Editor.Snapshot, ViewOptions() with { RootBlock = root });
            branches.Add(id, branch); activeBranch = branch; cadSelectionOrder = [];
            viewVersion = checked(viewVersion + 1); graph = null; PublishChange(); return CadState();
        }
    }

    /// <summary>Explicit close either atomically merges into the live source, or discards the
    /// detached branch. A changed target/branch or unresolved merge conflict cannot be overwritten.</summary>
    public object CadFinishBlock(CadBlockFinishRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            Check(request.Revision);
            var branch = activeBranch;
            if (branch?.BlockRoot is not { } root) throw new InvalidOperationException("There is no active block editor.");
            var changed = !ShadowGraphPatch.Between(branch.Ancestor, Current).Changes.IsEmpty;
            if (request.Save)
            {
                CheckMergeInputs(Current.DocumentId, branch, request.TargetRevision, request.Revision);
                DraftingBlocks.Definition(Current, root);
                var plan = liveEditor.PreviewMerge(branch.Ancestor, Current, cancellationToken: token);
                if (!plan.CanApply) throw new ShadowConflictException("The definition has conflicting live edits. Review the branch merge before saving.");
                if (changed) liveEditor.ApplyMerge(plan, "Edit block " + Current.Read(root)["Name"].Text);
            }
            else if (changed && !request.Discard) throw new InvalidOperationException("Confirm discarding the modified definition, or save it.");
            branches.Remove(branch.Id); RemovePlans(branch); activeBranch = null; cadSelectionOrder = [];
            liveEditor.Select([]); viewVersion = checked(viewVersion + 1); graph = null;
            PublishChange(); return CadState();
        }
    }
}
