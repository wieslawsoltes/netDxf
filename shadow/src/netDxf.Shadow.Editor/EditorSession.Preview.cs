using System.Collections.Immutable;

namespace netDxf.Shadow.Editor;

public sealed record ShadowPreviewOptions(int MaximumObjects = 250_000, int MaximumChanges = 100_000, int MaximumDetails = 500);
public sealed record ShadowPreviewChange(ObjectId Object, string Type, string Kind, ImmutableArray<string> Properties);
public sealed record ShadowPreviewSummary(int Added, int Removed, int Changed, bool DetailsTruncated, ImmutableArray<ShadowPreviewChange> Details);

/// <summary>An immutable graph preview bound to the exact target snapshot. Native validation runs only on apply.</summary>
public sealed class ShadowEditPreview
{
    public string Description { get; }
    public ShadowSnapshot Target { get; }
    public ShadowSnapshot Proposed { get; }
    public ShadowGraphPatch Patch { get; }
    public ShadowPreviewSummary Summary { get; }
    public string Validation => "GraphValidated; native source validation has not run";
    internal ShadowEditPreview(string description, ShadowSnapshot target, ShadowSnapshot proposed, ShadowGraphPatch patch, ShadowPreviewSummary summary)
    { Description = description; Target = target; Proposed = proposed; Patch = patch; Summary = summary; }
}

public sealed partial class EditorSession
{
    /// <summary>Builds a detached preview without changing source, history or selection.</summary>
    public ShadowEditPreview Preview(string description, ShadowEditBatch batch, ShadowPreviewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        lock (gate)
        {
            EnsureCurrent(); var snapshot = store.Current;
            CheckPreviewOptions(snapshot, options ?? new());
            return Preview(description, ShadowGraphPatch.Between(snapshot, snapshot.Apply(batch)), options);
        }
    }
    public ShadowEditPreview Preview(string description, ShadowGraphPatch patch, ShadowPreviewOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description); ArgumentNullException.ThrowIfNull(patch);
        if (description.Length > 512) throw new ArgumentException("Preview descriptions are limited to 512 characters.", nameof(description));
        lock (gate)
        {
            EnsureCurrent(); var snapshot = store.Current; options ??= new(); CheckPreviewOptions(snapshot, options);
            if (patch.Changes.Length > options.MaximumChanges) throw new ArgumentException("Preview change budget exceeded.", nameof(patch));
            // A proposed graph is not native history; do not borrow source history bindings.
            var detached = new ShadowGraphPatch(patch.DocumentId, patch.ExpectedRevision, patch.Changes);
            var proposed = detached.Apply(snapshot);
            if (proposed.Objects.Count > options.MaximumObjects) throw new ArgumentException("Preview object budget exceeded.", nameof(patch));
            var effective = patch.Changes.Where(c => !ShadowGraphPatch.Equivalent(c.Before, c.After)).ToArray();
            var details = effective.OrderBy(c => c.Id.Value, StringComparer.Ordinal).Take(options.MaximumDetails).Select(c =>
                new ShadowPreviewChange(c.Id, (c.After ?? c.Before)!.Type, c.Before is null ? "Added" : c.After is null ? "Removed" : "Changed",
                    (c.Before?.Properties.Keys ?? []).Union(c.After?.Properties.Keys ?? [], StringComparer.Ordinal)
                        .Where(p => !ShadowValue.Equivalent(c.Before?.Properties.GetValueOrDefault(p), c.After?.Properties.GetValueOrDefault(p)))
                        .Order(StringComparer.Ordinal).Take(128).ToImmutableArray())).ToImmutableArray();
            return new(description, snapshot, proposed, detached, new(effective.Count(c => c.Before is null), effective.Count(c => c.After is null),
                effective.Count(c => c.Before is not null && c.After is not null), effective.Length > details.Length, details));
        }
    }
    /// <summary>Atomically adopts a current preview as one canonical undo command, or leaves history unchanged on rejection.</summary>
    public ShadowSnapshot ApplyPreview(ShadowEditPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        lock (gate)
        {
            EnsureCurrent();
            if (!ReferenceEquals(store.Current, preview.Target)) throw new ShadowConflictException("The preview target changed. Create a new preview before applying.");
            return Execute(preview.Description, preview.Patch);
        }
    }
    private static void CheckPreviewOptions(ShadowSnapshot snapshot, ShadowPreviewOptions options)
    {
        if (options.MaximumObjects is < 1 or > 1_000_000 || options.MaximumChanges is < 1 or > 1_000_000 || options.MaximumDetails is < 1 or > 10_000)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (snapshot.Objects.Count > options.MaximumObjects) throw new ArgumentException("Preview object budget exceeded.");
    }
}
