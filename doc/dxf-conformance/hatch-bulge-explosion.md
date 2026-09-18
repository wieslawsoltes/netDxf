# Preserve bulge traversal when expanding HATCH polyline boundaries

`HatchBoundaryPath.Polyline.Explode()` now sets the arc orientation from the
bulge sign. Positive bulges traverse counterclockwise and negative bulges
traverse clockwise. The preceding implementation left the generated Arc's
IsCounterclockwise property at its false default. It also passed geometric
counterclockwise endpoint angles directly into a clockwise boundary record.

For a positive bulge, the existing ArcFromBulge start/end angles are retained
and IsCounterclockwise is true. For a negative bulge, that utility has already
exchanged geometric endpoints. The boundary therefore stores normalized
`360 - end` and `360 - start`, with IsCounterclockwise false. This follows the
existing clockwise convention used by HatchBoundaryPath.Arc.ConvertTo and
reproduced by the independent reader's real start/end points.

The correction applies both to direct Polyline.Explode calls and to polyline
edges expanded inside mixed boundary paths. A lone polyline boundary remains a
polyline; its storage is not needlessly converted. Input vertices are unchanged,
clone orientation is preserved, and existing zero-bulge line behavior remains.
The final open vertex's unused bulge is still ignored.

## Evidence and numerical limits

The same **225 new harness cases** pass **1 before / 225 after** this correction.
The negative run uses the unchanged final harness with the preceding preserved
library assembly (already containing the separate enumerable-input fix), not a
production stub. Model cases cover ten signed bulges from magnitude 0.125 to 4,
four quarter-turns, open/closed paths, source retention, arc conversion, and
clone agreement. The zero/dormant-bulge case preserves the existing line path.
Wire cases exercise signed minor/semicircular/major arcs inside mixed boundaries.

`tools/verify_hatch_bulge_explosion.py` validates **144 drawings** in the six
existing typed profiles R2000/R2004/R2007/R2010/R2013/R2018, text and binary. It
independently derives center, radius, and oriented endpoint angles from the
chord and bulge, checks the complete selected boundary packet, and rejects
**6,912** actual-tag changes, omissions, and duplications through the positive
validator. Every drawing has zero independent graph audit errors or repairs.

The independently loaded arc's real start/end points, closing edge, orientation,
and **4,752** traversal samples are checked against the source bulge, rather
than relying on a same-library round trip. Selected physical center/radius/angle
values admit absolute error `1e-10 * max(1, abs(expected))`; independently loaded
sample positions admit `1e-10 * max(1, radius, abs(center.X), abs(center.Y))`.
Other selected boundary codes, flags, order and closing-line coordinates are
exact. The corpus uses ordinary finite coordinates; these are conservative
absolute bounds, not a universal ULP or correctly rounded geometry claim.

The comparator covers the boundary-data packet from group 91 to before group
75, not every whole-document metadata field or native HATCH filling behavior.
Missing/extra fixture inventories reject. Full combined-suite, newline/inventory
controls and exact-head hosted qualification are recorded separately in the PR.

```sh
DXF_TEST_FILTER=hatch-bulge-explosion/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_hatch_bulge_explosion.py artifacts/conformance
```

## Primary references and remaining scope

Autodesk describes bulge as the tangent of a quarter of the included angle,
negative for clockwise traversal:
[Vertex2d.Bulge](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Vertex2d_Bulge.html).
The DXF [HATCH boundary-path schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm)
defines group 73 as the circular edge's counterclockwise flag.
The independent [ezdxf boundary model](https://ezdxf.readthedocs.io/en/stable/dxfentities/hatch.html)
distinguishes its internal counterclockwise geometry from actual path traversal.
These sources establish the representations, not native qualification of the
library's specific rounding or degeneration policies.

This task does not change ArcFromBulge arithmetic, the configurable near-zero
bulge policy, collapsed-chord/radius fallbacks, arbitrary extreme bulge admission,
OCS projection, hole classification, association ownership or HATCH tessellation.
No existing test or ownership guard is removed. Native AutoCAD open/AUDIT/save/
reopen, font/visual equivalence, historical typed dialects, private FIELD/TABLE/
cache regeneration, dependency-complete import and general version conversion
remain unqualified. This is not full AutoCAD/all-version DXF parity.
