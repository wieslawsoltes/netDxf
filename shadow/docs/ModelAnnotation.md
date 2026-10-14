# Model-tab annotation context

`RenderCompileOptions.ModelAnnotation` carries an immutable, source-neutral `ModelAnnotationContext`.
The Model layout multiplies the existing entity/global/consumer linetype scale by drawing units per paper unit
when model annotation scaling is active. This affects signed dashes and complex symbols together, before
endpoint fitting. It does not scale geometric entity coordinates, semantic graph edges or ordinary TEXT entities.

## Resolution

`Drawing` respects captured `$MSLTSCALE` (group 70, 0 or 1). Missing switches default to disabled for
pre-2008 DXF versions and enabled for modern drawings. `Disabled` bypasses this layer; `Enabled` overrides
the drawing switch. Explicit `AnnotationScale(PaperUnits, DrawingUnits)` takes precedence over captured
scale selection. All units and the resulting multiplier must be positive and finite.

Otherwise a captured `$CANNOSCALEVALUE` real ratio supplies paper units per drawing unit (the multiplier is its
reciprocal). A captured `$CANNOSCALE` name is looked up in a host-provided immutable `NamedScales` catalog.
Display names are never parsed as ratios: a name such as `1:50` need not describe its actual stored units.
The standard absent/default context is 1:1. An unresolved named context produces a `linetype.annotation`
diagnostic and multiplier 1 rather than guessing units. Duplicate or malformed relevant custom headers are rejected.

The custom-header adapter from #247 supplies the detached `CustomVariables` collection and source-backed
edits/undo. Native SCALE dictionary objects are not automatically decoded by this consumer; hosts can supply
their authoritative named catalog or explicit units. This is a model-tab linetype context, not a complete
annotative entity/context-object implementation.

```csharp
var options = new RenderCompileOptions
{
    ModelAnnotation = new(new AnnotationScale(1, 50), ModelAnnotationMode.Enabled)
};
var scene = compiler.Compile(snapshot, options);
```

All context values participate in immutable option cache identity. Source-header edits create new snapshots.
Retained snapshots and old options continue to produce their own output. Context resolution never writes headers.

## Paper-space isolation

Paper layout entities are unaffected. Internal model compilation for paper viewports explicitly uses
`ModelAnnotation = null`; only the existing PSLTSCALE and viewport compensation apply there. Model-tab
annotation and paper compensation therefore cannot multiply each other accidentally.

## Studio

The Model annotation selector, paper/drawing units and Apply scale button are workspace-local view controls.
The guarded `/api/annotation-view` mutation checks document/workspace/view/revision identities through the
existing request boundary. A real change advances the view epoch and clears stale selection without changing
the source revision or undo history. A repeated value is a no-op. Forks inherit the current view setting;
switching workspaces restores each workspace's setting. Loading a new document resets the live context.

## Verification and references

`ModelAnnotationVerification` adds 22 unique advanced cases for explicit/named/drawing context, modern/legacy
defaults, invalid values, source history, cache retention, paper isolation and Studio workspaces. The HTTP
contract suite exercises the actual guarded route, stale epochs and source-neutral no-ops.

Autodesk references: [MSLTSCALE](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-023B046C-56EA-463C-A867-DF713666A69E.htm),
[CANNOSCALE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-Core/files/GUID-C7111BEE-76AA-481D-AC51-8600A31FA23A.htm).
This implementation preserves the existing source codec and does not claim native SCALE-dictionary or
proprietary annotation-object certification.
