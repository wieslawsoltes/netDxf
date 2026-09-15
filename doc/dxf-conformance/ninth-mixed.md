# Native cell-style maps, explicit section membership and mesh references

This mixed graph combines the public SECTION_MANAGER membership operation with
native CELLSTYLEMAP resource dependencies, SECTIONSETTINGS source pointers,
retained PolygonMesh child identities, a named VIEW reference, and target-version
diagnostic snapshots. It exercises reference release and rejection across the
same graph instead of treating coexisting objects as independent examples.

Two hash-pinned whole native TABLECONTENT source drawings provide R2013 and
R2018 coverage: `acad_table_simple.dxf` and `acad_table_with_blk_ref.dxf`. Their
CELLSTYLEMAP records remain attached to the original TABLESTYLE extension
dictionary and retain actual STYLE `11` and LTYPE `14` references. Every mixed
output compares the complete CELLSTYLEMAP and LTYPE records to the originals.
The complete STYLE comparison accounts for one established writer normalization:
the empty group 4 is omitted and public fields are ordered
`2,3,70,71,40,41,42,50`; every stored value and the trailing ACAD XData remains
exact. This is not a claim that native STYLE packet ordering is retained.

The added SECTIONs, SECTION_MANAGER packet, VIEW and ordinary 2-by-2 PolygonMesh
are explicitly authored. Each SECTION owns a settings object whose single type
bundle contains these exact generic source-pointer slots:

1. The native CELLSTYLEMAP object.
2. The mesh's first retained VERTEX.
3. An explicit null handle.
4. The same VERTEX again.
5. The native STYLE object.
6. The native LTYPE object.

`DxfSectionTypeSettings` admits registered `DxfObject` identities in these slots
and preserves nulls, order and repetitions. These links qualify generic pointer
storage only. No section generation, rendering behavior, geometric evaluation or
CAD application interpretation is inferred from their presence.

The initial manager list is `[first, second, first]` with its update flag set.
Replacing it with `[second, second]` and clearing the flag releases one incoming
dependency, while the named VIEW still prevents erasing the first SECTION.
Clearing the VIEW allows erasing that SECTION and its owned settings, but the
second settings object still prevents removal of the referenced mesh child.
After a coordinate edit and U-closure edit preserve the retained child identities,
the second settings list is explicitly shortened to `[map, STYLE, LTYPE]`.
Only then can the mesh be removed. Its child records leave document registration
and remain structurally owned by the detached mesh. The manager's repeated second
SECTION and the native map resource graph remain intact.

Target-version analysis runs during caller enumeration, before the manager edit
commits, without changing the drawing profile or allocating handles. The R2004
snapshot identifies actual map, manager and mesh records with known source-profile
rejections. After a SECTION is erased and mesh records leave registration, the
old diagnostics retain their captured code, kind, handle, code name, property
path and message, and their `SourceObject` properties still refer to the same
actual objects. A fresh analysis excludes the unregistered sources and retains
diagnostics for the surviving source-bound objects. The report is a snapshot of
known writer constraints, not a complete legality or save guarantee.

Separate rejection cases attempt foreign SECTION membership, map erasure,
extension-graph cloning and an unsupported output profile. They require unchanged
manager/map packets, coordinates, child identities, object membership and handle
seed. Save preflight must reject before writing any bytes. Restoring the original
profile must allow a successful save and a report without known rejections.

The eight cases cover both native source profiles and ASCII/binary transports.
The independent gate requires exactly 16 stage drawings: input, edited, released
and successful retry for each source/transport combination. It checks complete
raw records and declared fields, including SECTION planes, heights, vertex counts
and appearance; settings count/order/null targets and reciprocal ownership; mesh
U/V dimensions, flags, dummy point, normal, retained coordinates, VERTEX metadata
and SEQEND ownership; root anchors; and exact CLASS metadata/counts. It rejects
300 actual parsed-output corruptions, including consistent identity rewrites of
managers, surviving SECTIONs and their settings, plus replacement of a released
mesh under entirely new identities. Its checks run before and alongside ezdxf
auditing, so normalized high-level entities cannot hide changed raw fields.
Both Debug and Release pass all eight cases and the 16-output, 300-control gate
with zero audit errors and zero repairs. The [qualification receipt](ninth-mixed-qualification.json)
records the frozen sources, separate C# build revision, libraries, native inputs,
results and every output hash. The full combined suite and other target frameworks
remain the responsibility of integration qualification.

```sh
DXF_TEST_FILTER=ninth-mixed DXF_TEST_ARTIFACTS=/path/to/artifacts \
  dotnet tests/netDxf.Conformance/bin/Debug/net8.0/netDxf.Conformance.dll
python tools/verify_ninth_mixed.py /path/to/artifacts
```

This scope does not qualify complete-drawing byte identity, native AutoCAD
execution, automatic section membership, new manager construction, native
generation of the authored source-pointer graph, or mesh topology editing.
