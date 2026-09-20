# Raw ELLIPSE geometry without angle conversion

`ReadEllipseGeometry(record)` exposes immutable `DxfRawEllipseGeometry`.
`WithEllipseGeometry(record, center, majorAxis, axisRatio, startParameter,
endParameter)` changes only the stored center, major-axis vector, ratio and
parameters. The existing extrusion is retained. Both vectors are WCS-valued:
the major semi-axis is relative to the center, not an absolute endpoint or a
full diameter. Parameters are not degree-valued polar angles.

```csharp
DxfRawEllipseGeometry ellipse = raw.ReadEllipseGeometry(record);
DxfRawDocument edited = raw.WithEllipseGeometry(record,
    new Vector3(8, -16, 32), ellipse.MajorAxis, ellipse.AxisRatio,
    ellipse.StartParameter, ellipse.EndParameter);
```

## Admission and preservation

Eight existing raw families, AC1012 through AC1032, are supported. AC1009
geometry interpretation rejects; generic raw tag preservation is unchanged.
Classic markerless and AcDbEntity/AcDbEllipse layouts are admitted in ENTITIES
and BLOCKS. Required fields are both X/Y pairs, axis ratio, start and end
parameters. Missing Z components default to zero, and missing extrusion
components use (0,0,1). Duplicate geometry, malformed controls, ambiguous
subclasses and ordinary fields after XData reject.

Axes and extrusion must be finite and nonzero, with absolute normalized dot
product at most 1e-12. Component scaling keeps this admission check valid at
very small and large magnitudes. Neither vector is renormalized or projected
in stored output. Ratio must be in (0,1]; no additional minimum is imposed.
Parameters must be finite but are not normalized, reordered or converted to
angles. Reversed and out-of-range parameters retain their original values;
this operation does not certify their native sweep interpretation. Tiny ratios
or axes can imply unrepresentable derived geometry: the raw operation stores
components, it does not guarantee all computed curve points are representable.

The plane bound and strict required-field policy are library contracts, not
claims that AutoCAD applies the same tolerance or rejects the same files.
MathHelper.Epsilon has no effect on these checks.

Bit-identical edits return the original snapshot and retain original-byte
output. Actual edits replace existing slots in place. A missing Z appears
after its Y only when its new value is not positive zero. Negative zero and
subnormal components are preserved. Every unrelated tag retains object
identity, value and relative order; the source snapshot remains unchanged.
Changed serialization may normalize numeric spelling and line endings.

Actual changes use existing proxy/private/application/embedded/XData and
incoming-handle guards. Some benign references reject conservatively. Hidden
binary or string dependencies, header extents, associations and private caches
are not regenerated. Tag and handle-index budgets and existing save/transport
contracts remain active. This is not full historical typed-document loading.

## Verification

Tests exercise eight profiles, text/binary input and output, ENTITIES/BLOCKS,
markerless records, omitted defaults, tilted/reversed/nonunit extrusion,
full ellipses and reversed intervals, finite extremes, negative zero,
malformed input, private context, incoming references and tag budgets.
Six modern typed profiles additionally round-trip full ellipses.

The independent checker regenerates input geometry, compares complete
before/after tag sequences, checks physical version and transport, and uses
an independent reader to evaluate 17 parametric curve positions per drawing.
Its matrix comprises 256 source/edit pairs (512 drawings). Actual-tag
mutations and missing/extra inventories must fail the same validators.
The new corpus is synthetic rather than native producer evidence.

```sh
DXF_TEST_FILTER=raw-ellipse/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_ellipse_geometry.py artifacts/conformance
```

Execution counts and platform results are recorded in the PR and delivery
receipt. No local C# execution is inferred when a local SDK is unavailable.

## Primary reference and outstanding work

Autodesk's [ELLIPSE schema](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm)
specifies WCS center/relative major axis, extrusion, ratio and parameters.
No new historical typed dialect, pre-R11 format, general version conversion,
private FIELD/TABLE regeneration, dependency-complete import, native font or
visual qualification, or native AutoCAD open/AUDIT/save/reopen is established.
