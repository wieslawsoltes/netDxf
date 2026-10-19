# netDxf.Shadow.Health

Read-only diagnostics over immutable `ShadowSnapshot` data. The library depends on Shadow and Geometry; it has no native DXF, renderer, UI, filesystem, or provider dependency.

```csharp
using System.Collections.Immutable;
using netDxf.Shadow.Health;

var report = DrawingHealth.Analyze(snapshot,
    new DrawingHealthOptions(TinyLength: 1e-7, GapTolerance: 0.01),
    new DrawingHealthResources(
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "txt.shx")),
    cancellationToken);

foreach (var issue in report.Issues)
    Console.WriteLine($"{issue.Severity}: {issue.Code} {issue.Object} {issue.Path}: {issue.Message}");
// Complete=false means a work, object, depth, or issue budget prevented a full scan.
```

`snapshot` and `cancellationToken` are supplied by the caller. Omit `DrawingHealthResources` when SHX availability is unknown; an empty inventory explicitly means no SHX font is available. Matching is case-insensitive and accepts a registered basename for a full font path. Optional presentation diagnostics can be included without coupling Health to a renderer.

Checks include dangling references, capture faults, entity-index consistency, multiple block ownership, nonfinite numeric values, common geometry domains, recursive INSERT definitions, tiny lines, exact same-owner geometry duplicates, nearby open endpoints, and unused block/style/linetype resources. Findings contain source IDs, paths, related objects, and optional positions. Results are deterministic for the same snapshot/options/inventory; no repair or deletion occurs.

Default budgets are 250,000 objects, 5,000,000 work units, 2,000 issues, and 64 nested value levels. `GapTolerance` must be finite and greater than the nonnegative `TinyLength`; distances use drawing units. Cancellation throws `OperationCanceledException`. Enabled families are described in `Coverage`; `Limitations` explains incomplete geometric coverage.

Duplicate checks compare LINE/CIRCLE/ARC/ELLIPSE/POINT geometry, including reversed LINE endpoints, within a single owner. They do not compare appearance or detect arbitrary overlapping curves. Tiny gaps are 3D endpoint suggestions for LINE, ARC, and open 2D/3D polylines, within an owner; block instances are not flattened into world-space connectivity. Native source acceptance, arbitrary intersections, spline/hatch validity, external references, and installed TrueType availability are outside this library.

See [Drawing health, edit previews and workflow regressions](../../docs/DrawingHealthAndWorkflows.md) for Studio selection, review and replay integration.
