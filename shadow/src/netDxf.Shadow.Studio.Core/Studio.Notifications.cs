using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace netDxf.Shadow.Studio;

/// <summary>A coalescible invalidation, not a lossless transaction log or serialized drawing.</summary>
public sealed record StudioRevisionNotice(string ServerId, long ChangeVersion, Guid DocumentId,
    string WorkspaceId, long ViewVersion, long Revision, long SourceRevision, bool Reset)
{
    [JsonIgnore] public string EventId => ServerId + ":" + ChangeVersion.ToString(CultureInfo.InvariantCulture);
}

public sealed class StudioSubscription : IDisposable
{
    private Action? release;
    public ChannelReader<StudioRevisionNotice> Reader { get; }
    internal StudioSubscription(ChannelReader<StudioRevisionNotice> reader, Action release)
    { Reader = reader; this.release = release; }
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}

public sealed partial class Studio
{
    private readonly string serverId = Guid.NewGuid().ToString("N");
    private long changeVersion;
    private long subscriptionSequence;
    private readonly Dictionary<long, Channel<StudioRevisionNotice>> subscriptions = new();
    public int ActiveSubscriptions { get { lock (gate) return subscriptions.Count; } }

    /// <summary>
    /// Registration and the initial notice are atomic with publication. Each client holds at
    /// most one pending notice; slow readers receive the newest state and fetch /api/state.
    /// Reconnects always receive an initial notice, including after document replacement.
    /// </summary>
    public StudioSubscription Subscribe(string? lastEventId = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (subscriptions.Count >= 32) throw new InvalidOperationException("The local sample permits at most 32 event subscribers.");
            var channel = Channel.CreateBounded<StudioRevisionNotice>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true,
                SingleWriter = true, AllowSynchronousContinuations = false
            });
            var id = checked(++subscriptionSequence);
            var notice = Notice();
            channel.Writer.TryWrite(notice with { Reset = !string.Equals(lastEventId, notice.EventId, StringComparison.Ordinal) });
            subscriptions.Add(id, channel);
            return new(channel.Reader, () =>
            {
                lock (gate) if (subscriptions.Remove(id, out var removed)) removed.Writer.TryComplete();
            });
        }
    }

    private StudioRevisionNotice Notice() => new(serverId, changeVersion, Current.DocumentId,
        WorkspaceId, viewVersion, Current.Revision, session.Current.Revision, false);
    private void PublishChange()
    {
        CheckWorkflowPublication();
        changeVersion = checked(changeVersion + 1);
        if (subscriptions.Count == 0) return;
        var notice = Notice();
        foreach (var channel in subscriptions.Values) channel.Writer.TryWrite(notice);
    }
    private object UpdatedState() { PublishChange(); return State(); }
    private void CompleteSubscriptions()
    {
        foreach (var channel in subscriptions.Values) channel.Writer.TryComplete();
        subscriptions.Clear();
    }
}
