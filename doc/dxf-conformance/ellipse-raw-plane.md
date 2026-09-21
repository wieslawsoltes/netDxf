# Explicit raw ELLIPSE plane editing

`DxfRawDocument.WithEllipseGeometryAndPlane` edits a complete raw ELLIPSE
geometric definition, including the extrusion direction. The existing
`WithEllipseGeometry` method retains its existing-plane behavior and signature;
both use one shared validated edit engine. A distinct method name also preserves
unambiguous reflection lookup for existing consumers.

```csharp
DxfRawEllipseGeometry previous = raw.ReadEllipseGeometry(record);
DxfRawDocument edited = raw.WithEllipseGeometryAndPlane(record,
    new Vector3(8, -16, 32),   // WCS center
    new Vector3(3, 2, 0),     // Relative WCS major semi-axis, not an endpoint
    0.25,                    // Minor / major ratio
    previous.StartParameter, previous.EndParameter,
    new Vector3(2, -3, 6));   // Perpendicular to the major axis; retained exactly
```

This is explicit definition editing, not an affine transform or a promise of
shape preservation. Changing extrusion changes the plane; reversing it reverses
the derived minor-axis direction. Parameters are not swapped, normalized,
converted from polar angles or used to refit a curve. Center and major axis are
WCS quantities, not OCS coordinates. The major vector is relative to the center
and has semi-axis length, not diameter length.

## Admission and preservation

Eight existing raw profiles, AC1012 through AC1032 (R13 through R2018), support
this operation in text/binary and ENTITIES/BLOCKS. R12/AC1009 rejects this semantic
operation; generic raw preservation is unchanged. The existing packet parser,
subclass/private-data framing and required-field rules remain unchanged.

Center, major vector, extrusion, ratio and parameters must be finite. Major and
extrusion must be nonzero and perpendicular under the existing component-scaled
normalized-dot bound of 1e-12, independent of MathHelper.Epsilon. Ratio is in
(0,1]. Stored/replacement vectors are not normalized. These are library admission
rules, not a claimed native AutoCAD tolerance policy.

An exact bit-identical no-op returns the original snapshot and preserves original
bytes. Actual edits first validate every value, dependency guard and tag budget.
They replace changed center/axis/ratio/parameter slots in place. Missing
center/axis Z is added immediately after its Y only when different from positive
zero. A changed extrusion is written as one complete, adjacent 210/220/230 vector,
including zero/default components. The vector occupies the first existing
extrusion slot, or follows the last geometry field when no extrusion is stored.
Partial or reordered extrusion components are regrouped, while unchanged stored
components reuse their tag objects. All non-extrusion tags retain relative order
and unchanged values retain object identity, including application-neutral XData.
An unchanged extrusion packet remains untouched, including absent defaults.
Negative zero differs from positive zero. Repeating the resulting exact definition
is a snapshot-identity no-op.

The first independent run exposed a real interoperability defect in the initial
implementation: omitting default-valued group 210 while inserting only 220/230
left the independent reader at its default +Z plane. This was corrected in
production, not ignored by the checker. The raw budget accounts for every needed
vector component. Added cases cover absent, partial, reversed and separated
source extrusion groups; changed output is required to contain a complete vector.
This is a deliberate difference between source-preserving no-ops and authored
plane changes, not a promise to normalize every malformed source packet.

Source snapshots remain immutable. Changed output may normalize textual spelling
and line endings. Proxies, application/embedded data, unknown fields,
geometry-sensitive XData and exposed incoming handle dependencies conservatively
block real edits. No-op behavior and the preceding geometry-only API remain
covered. Hidden references, header extents, native caches and associations are
not regenerated. Existing raw byte/tag/string budgets and save restrictions
remain; no new global process-memory or CPU guarantee is asserted.

## Qualification

The new harness covers the eight-profile/input-transport/placement/plane matrix,
all output transports, normal-only edits, unchanged tag identities, exact no-ops,
source isolation, optional/default/signed-zero components, partial extrusion
packets, required tag-budget exhaustion, reversed/nonunit/subnormal extrusion,
finite admission, dependency guards, snapshot errors and old-API equivalence.
Ordinary full ellipses also pass through all six existing typed profiles.

The independent checker regenerates source and expected edited packets, compares
retained fields and independently samples WCS curves in both snapshots. It rejects
actual changed/deleted/repeated tags and missing/extra inventories. The fixture
matrix produces 256 source/edit pairs plus 16 vector-packet drawings (528 drawings),
not native producer files.
Exact-head executed C# and independent results are recorded in the PR. Python
syntax validation alone is not C# qualification. Extreme raw-value tests do not
claim native or independent geometric acceptance at those extremes.

```sh
DXF_TEST_FILTER=ellipse-raw-plane/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_ellipse_raw_plane.py artifacts/conformance
```

Autodesk's [ELLIPSE reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-107CB04F-AD4D-4D2F-8EC9-AC90888063AB.htm)
defines the physical center, relative major vector, ratio, extrusion and
parameters. Independent R13/R14 in-memory reader upgrades are not native AutoCAD
execution; the physical declared profiles are checked separately. This adds no
historical typed dialect, pre-R11 profile, general version conversion, complete
private FIELD/TABLE/cache regeneration, dependency-complete import or native
AutoCAD open/AUDIT/save/reopen, visual or font qualification.

The [ezdxf tag-format documentation](https://ezdxf.readthedocs.io/en/stable/dxfinternals/dxftags.html)
explains the reader's component adjacency requirement. Complete-vector emission
is qualified here against that independent reader as well as physical raw tags;
it is not proof that every native application accepts all retained source forms.
