namespace netDxf.Shadow.NetDxf;

public sealed partial class ShadowSession
{
    // The monitor excludes other threads but is reentrant on the current thread.
    // Source callbacks must not publish or start another transaction while a capture,
    // validation, source write, reconciliation or rollback is in progress.
    private bool sourceOperationActive;

    private SourceOperation EnterSourceOperation()
    {
        EnsureHealthy();
        if (sourceOperationActive)
            throw new InvalidOperationException(
                "Reentrant source operations are not allowed. Read Current in callbacks and apply further changes after the outer operation returns.");
        sourceOperationActive = true;
        return new SourceOperation(this);
    }

    private void EnsureSourceCurrent()
    {
        // A successful no-change capture returns the identical immutable snapshot.
        // A conflict is neither a publication nor an implicit rebase: leave Current,
        // retained snapshots and editor history unchanged until the caller refreshes.
        if (!ReferenceEquals(capture.Capture(document, current), current))
            throw new ShadowConflictException(
                "Source changed outside the session. Refresh before applying a transaction.");
    }

    // Stack-local scope: no allocation on the scalar edit path and reset on every exit,
    // including validation failures and exceptions from caller-supplied callbacks.
    private readonly struct SourceOperation(ShadowSession session) : IDisposable
    {
        public void Dispose() => session.sourceOperationActive = false;
    }
}
