using System.Runtime.ExceptionServices;

namespace netDxf.Shadow.Editor;

public sealed partial class EditorSession
{
    private bool sourceSynchronization;

    /// <summary>
    /// Runs an explicitly chosen, serialized source normalization (for example DXF writer
    /// housekeeping) and adopts its reconciled snapshot without adding an editor command.
    /// The callback must publish through the editor's store, return its exact Current, and
    /// must not reenter editor transactions. A failing callback must still reconcile its store.
    /// </summary>
    /// <remarks>
    /// This is not automatic acceptance of external user edits. Use a merge or ResetHistory
    /// for those. Property history keeps optimistic before/after preconditions; structural
    /// undo restores its recorded graph, including earlier normalization metadata. The
    /// source adapter still owns canonicalization and monotonic allocator rules. Neither
    /// source validation nor native rollback is disabled by this editor-side operation.
    /// </remarks>
    public ShadowSnapshot SynchronizeSource(Func<ShadowSnapshot> synchronize)
    {
        ArgumentNullException.ThrowIfNull(synchronize);
        lock (gate)
        {
            EnsureCurrent();
            var before = store.Current;
            ShadowSnapshot? result = null;
            Exception? failure = null;
            sourceSynchronization = true;
            try { result = synchronize(); }
            catch (Exception error) { failure = error; }
            finally { sourceSynchronization = false; }
            var after = store.Current;
            if (after.DocumentId != before.DocumentId || after.Root != before.Root || after.Revision < before.Revision)
            {
                ResetHistory(clearSelection: true);
                throw new ShadowConflictException("Source synchronization replaced the document or moved backwards. Editor history was reset.");
            }
            // Even failed normalization can have reconciled source housekeeping. Preserve
            // the original failure, but do not leave the editor at an obsolete revision.
            expectedRevision = after.Revision;
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            if (!ReferenceEquals(result, after)) throw new InvalidOperationException("Source synchronization must return the store's exact current snapshot.");
            return after;
        }
    }
}
