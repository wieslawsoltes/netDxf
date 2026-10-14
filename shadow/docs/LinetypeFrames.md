# Complex-linetype glyph frames and rotation pivots

Two opt-in `LinetypePlanOptions` policies extend the shared dash/symbol engine without changing its original defaults.

## Definition-space metric

`MetricSpace = LinetypeMetricSpace.Definition` measures the sampled path after applying the inverse source
occurrence transform. It fits and places both dashes and symbols in that definition frame, then transports
the complete result through the original affine transform. Nonuniform scale, reflection, shear, rotation
and translation therefore affect stroke lengths, symbol widths/heights, skew, offsets and rotation pivots
together. Nested insert transforms are already composed in `SourceToWorld`.

The existing `World` mode remains the default and retains constant world-space pattern size. The definition
policy is explicit because CAD producers differ in how inherited pattern scales interact with inserts; it
is not an assertion that every proprietary insert convention is identical. Absolute glyph rotation in
definition mode is absolute in the definition's authored plane, then transformed with the instance.

`RenderItem.PatternToWorld` carries the metric transform until the stroke pattern has been resolved.
Paper projection resolves definition-space dashes before projection/depth clipping and resets the metric
on its already-compiled output. Symbols and dashes therefore cannot be transformed twice.
Source identities and editing transforms remain the original source-to-world transform; glyph placement
does not become an editing coordinate system.

## Text rotation pivot

`TextRotationPivot = LinetypeTextRotationPivot.NominalCapHeightCenter` rotates text around half the nominal
authored cap height instead of the baseline origin. Style obliquing and upside-down orientation contribute
to that unit-height pivot. An explicit finite, planar `TextPivot` overrides the nominal choice.
The unrotated pivot is carried to the same point after relative/absolute/upright rotation, including flips.
Tangent-relative offsets are independent of glyph scale; shape insertion origins are not reinterpreted as text pivots.

`BaselineOrigin` remains the default for existing consumers. These options are set on the render compiler:

```csharp
var compiler = new RenderCompiler(builder, textLayout,
    linetypeOptions: new()
    {
        MetricSpace = LinetypeMetricSpace.Definition,
        TextRotationPivot = LinetypeTextRotationPivot.NominalCapHeightCenter
    });
```

## Invariants and numerical limits

Coordinate copies are preflighted against the point quota, checked for finite input/output and cancellable.
Singular source transforms, invalid enum values and nonplanar/nonfinite pivot overrides fail explicitly,
rather than silently falling back to world spacing. Distances remain render-tolerance-dependent sampled
arc lengths; transforming a sampled path does not certify exact analytic arc length.
The nonplanar parallel-transport policy can be combined with definition-space metrics.

The neutral geometry, topology, source synchronization, cache keys and original public method signatures
are retained. Twenty unique advanced cases cover cap-height and explicit pivots, reversed/upright text,
shape origins, multiple affine matrices, patterned curves, native text, source topology, paper projection
and malformed/bounded input.

Autodesk's [Text in Custom Linetypes](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-LT-MAC-Customization/files/GUID-FEDCE7EB-4919-43AE-A54E-F3A293DD60CA.htm)
documents nominal cap-height pivots and independent offset scaling. This consumer exposes those choices
explicitly; undocumented producer-specific text fitting and raster-level certification are separate concerns.
