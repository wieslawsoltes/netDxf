using System.Text.Json;

namespace netDxf.Shadow.Studio;

/// <summary>Local ASP.NET hosts dispatch semantic UI work to their already-open Studio tab.</summary>
public sealed class StudioAgentUiRelay
{
    public sealed record Request(string Id, string Action, JsonElement Arguments, string ExpectedDocumentId);
    private sealed record Pending(Request Request, TaskCompletionSource<JsonElement> Completion);
    private readonly object gate = new();
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);

    public IReadOnlyList<Request> Poll() { lock (gate) return pending.Values.Select(p => p.Request).ToArray(); }
    public async ValueTask<JsonElement> InvokeAsync(string action, JsonElement arguments, string documentId, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (pending.Count >= 32) throw new InvalidOperationException("Too many pending Studio UI operations.");
            pending.Add(id, new(new(id, action, arguments.Clone(), documentId), completion));
        }
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), token).ConfigureAwait(false); }
        finally { lock (gate) pending.Remove(id); }
    }
    public bool Reply(string id, JsonElement? value, string? error)
    {
        lock (gate)
        {
            if (!pending.TryGetValue(id, out var request)) return false;
            if (error is not null) return request.Completion.TrySetException(new InvalidOperationException(error));
            return request.Completion.TrySetResult(value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null));
        }
    }
}
