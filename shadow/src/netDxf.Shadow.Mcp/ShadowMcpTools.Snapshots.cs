using System.Text.Json;
using netDxf.Shadow.Editor;

namespace netDxf.Shadow.Mcp;

public sealed partial class ShadowMcpTools
{
    private sealed record Checkpoint(string Id, string Name, ShadowMcpIdentity Identity, ShadowSnapshot Snapshot);
    private readonly object checkpointGate = new();
    private readonly List<Checkpoint> checkpoints = new();
    private const int MaximumCheckpoints = 8;

    private void InstallSnapshots(McpToolRegistry registry)
    {
        Add(registry, "shadow_checkpoint", "Retain an immutable snapshot in O(1) for later structural/property diff. Keeps at most eight snapshots per registry; oldest checkpoints expire. This does not mutate the drawing or editor history.", true,
            Fields(("name", String(128))), [], (a, e, s, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var checkpoint = new Checkpoint(Guid.NewGuid().ToString("N"), GetString(a, "name") ?? "Checkpoint", context.GetIdentity(s), s);
                lock (checkpointGate)
                {
                    checkpoints.Add(checkpoint);
                    if (checkpoints.Count > MaximumCheckpoints) checkpoints.RemoveAt(0);
                }
                return new { checkpoint = checkpoint.Id, checkpoint.Name, checkpoint.Identity, revision = s.Revision, capacity = MaximumCheckpoints };
            });
        Add(registry, "shadow_checkpoints", "List retained immutable snapshot checkpoints and their original document/workspace identities.", true, [], [], (a, e, s, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            lock (checkpointGate) return checkpoints.Select(c => new { checkpoint = c.Id, c.Name, c.Identity, revision = c.Snapshot.Revision }).ToArray();
        });
        Add(registry, "shadow_diff", "Compare the current shadow graph against a retained checkpoint, including branches of the same document. Page added/removed/changed nodes and exact changed property names. Optionally include bounded before/after values.", true,
            Fields(("checkpoint", String(128)), ("includeValues", Boolean()), ("offset", Integer(0)), ("limit", Integer(1, 100))), ["checkpoint"], DiffCheckpoint);
    }

    private object DiffCheckpoint(JsonElement args, EditorSession editor, ShadowSnapshot snapshot, CancellationToken token)
    {
        Checkpoint checkpoint;
        lock (checkpointGate) checkpoint = checkpoints.FirstOrDefault(c => c.Id == RequiredString(args, "checkpoint"))
            ?? throw new ArgumentException("Checkpoint expired or is unknown. List shadow_checkpoints or create a new checkpoint.");
        if (checkpoint.Snapshot.DocumentId != snapshot.DocumentId) throw new ArgumentException("Cannot compare checkpoints from unrelated documents.");
        token.ThrowIfCancellationRequested();
        var delta = snapshot.Diff(checkpoint.Snapshot);
        var include = GetBool(args, "includeValues");
        var changes = delta.Added.Select(id => (Id: id, Kind: "added"))
            .Concat(delta.Changed.Select(id => (Id: id, Kind: "changed")))
            .Concat(delta.Removed.Select(id => (Id: id, Kind: "removed")))
            .OrderBy(c => c.Id.Value, StringComparer.Ordinal);
        return new
        {
            checkpoint = checkpoint.Id, fromIdentity = checkpoint.Identity, fromRevision = checkpoint.Snapshot.Revision, toRevision = snapshot.Revision,
            added = delta.Added.Count, changed = delta.Changed.Count, removed = delta.Removed.Count,
            changes = Page(changes, GetInt(args, "offset", 0), GetInt(args, "limit", 50), change =>
            {
                token.ThrowIfCancellationRequested();
                checkpoint.Snapshot.Objects.TryGetValue(change.Id, out var before);
                snapshot.Objects.TryGetValue(change.Id, out var after);
                var properties = (before?.Properties.Keys ?? []).Union(after?.Properties.Keys ?? [], StringComparer.Ordinal)
                    .Where(name => !ShadowValue.Equivalent(before?[name], after?[name])).Order(StringComparer.Ordinal).ToArray();
                return new
                {
                    id = change.Id.Value, change.Kind, type = after?.TypeName ?? before?.TypeName,
                    propertyCount = properties.Length,
                    properties = properties.Take(100).Select(name => new
                    {
                        name, path = "/" + ShadowJson.Escape(name),
                        before = include && before is not null ? ShadowJson.Describe(before[name], 1, 4, textLimit: 128) : null,
                        after = include && after is not null ? ShadowJson.Describe(after[name], 1, 4, textLimit: 128) : null
                    }).ToArray(),
                    propertiesTruncated = properties.Length > 100
                };
            })
        };
    }
}
