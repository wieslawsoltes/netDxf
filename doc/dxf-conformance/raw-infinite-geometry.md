# Raw RAY/XLINE origins and unit directions

`ReadRayGeometry` and `ReadXLineGeometry` expose immutable
`DxfRawInfiniteLineGeometry` values with a world-coordinate origin and direction.
`WithRayGeometry(record, origin, unitDirection)` and `WithXLineGeometry` replace
those fields in a new same-version raw snapshot. The direction is **not a second
point**. No endpoint subtraction, OCS conversion, normalization or sign reversal
is performed. RAY traversal therefore remains meaningful; XLINE direction sign
also retains the caller's exact stored representation.

```csharp
DxfRawInfiniteLineGeometry ray = raw.ReadRayGeometry(record);
DxfRawDocument edited = raw.WithRayGeometry(record,
    new Vector3(10, 20, 30), ray.Direction);
```

## Definition and preservation

The supported raw profiles are AC1012, AC1014, AC1015, AC1018, AC1021, AC1024,
AC1027 and AC1032 (R13 through R2018). AC1009/R11/R12 geometry operations reject;
the generic raw snapshot's tag-preservation behavior is unchanged. This does
not enable historical typed `DxfDocument` loading or a new version converter.

Classic markerless records and AcDbEntity followed by AcDbRay/AcDbXline are
admitted in ENTITIES and BLOCKS. Records must belong to the exact snapshot.
Origin and direction X/Y components are required. Omitted Z components default
to positive zero. Duplicate components, ambiguous subclass/control framing and
ordinary data after XData reject. Private application groups and genuine
embedded tails cannot supply missing core geometry or override stored values.

Stored and replacement directions must be finite and have squared length within
`1e-12` of one. This absolute, MathHelper.Epsilon-independent admission bound is
a library policy, not a claim about native AutoCAD's rounding or tolerance.
Values inside the bound are preserved bit-for-bit rather than renormalized.
Zero, nonfinite and clearly nonunit directions reject. Finite origins may include
subnormals and extreme binary64 values; no additional geometric operation is
needed to store them.

Existing geometry fields are replaced in place. Missing Z fields are inserted
immediately after the corresponding Y only when the requested value is not
positive zero. Negative zero is preserved. All untouched DxfTag objects retain
their identities, values and relative order. A bit-identical edit returns the
original snapshot, including its original-byte output. Real edits leave the
source unchanged but use the existing serializer, which may normalize lexical
number spelling or line endings.

## Dependency and resource boundaries

Real changes use the existing conservative proxy/private/application/embedded
and geometry-sensitive XData guards, plus the raw handle index's exposed incoming
reference checks. No-op edits still validate the selected schema and unit
geometry but do not need dependency regeneration. Unknown ordinary fields,
extrusion or thickness packets, malformed/duplicate identities and exposed
incoming references may prevent editing rather than being silently discarded.
Some benign reference uses therefore reject conservatively.

Header extents, associations, hidden binary/string references and private caches
are not regenerated. Output is a selected schema edit, not a dependency-complete
or whole-document semantic certificate. Existing raw byte/tag/string limits,
handle-index limits, caller stream ownership, atomic-file-save and transport
restrictions remain unchanged. Missing-Z insertion checks the total tag budget
before constructing a replacement snapshot.

## Verification

The focused harness has 406 cases covering both entities, eight raw profiles,
text/binary input and output, ENTITIES/BLOCKS, markerless records, omitted Z,
reversed directions, finite extremes, unit tolerance, no-op/source preservation,
invalid input, private contexts, dependencies and tag budgets. Six modern typed
profiles separately round-trip through DxfDocument. The unchanged harness was
executed against the preceding actual DLL without these APIs; its 406 failures
are API availability evidence, not a claim of 406 pre-existing bugs.

`verify_raw_infinite_geometry.py` regenerates expected source geometry, compares
complete before/after tag sequences, verifies physical $ACADVER and binary
framing, and independently decodes WCS origins and unit vectors with ezdxf.
The corpus contains 512 source/edit pairs (1,024 drawings). Corruption controls
change, remove or repeat actual selected tags and change neighboring records;
missing/extra inventories must fail the same positive validators. The fixtures
are synthetic rather than native AutoCAD producer evidence. Final execution,
corruption totals and graph-audit results are recorded in the PR and receipt.

```sh
DXF_TEST_FILTER=raw-infinite/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_infinite_geometry.py artifacts/conformance
```

Autodesk's [RAY](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-638B9F01-5D86-408E-A2DE-FA5D6ADBD415.htm)
and [XLINE](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-55080553-34B6-40AA-9EE2-3F3A3A2A5C0A.htm)
references specify WCS origins and unit directions. Snapshot identity, tolerance,
strict missing-field admission and exception behavior are library contracts.
Full historical typed loading, pre-R11 profiles, native AutoCAD
open/AUDIT/save/reopen, full private FIELD/TABLE/cache regeneration,
dependency-complete import, general version conversion and font/visual
qualification remain separate work. This is not full all-version AutoCAD parity.
