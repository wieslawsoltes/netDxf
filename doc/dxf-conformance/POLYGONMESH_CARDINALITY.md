# PolygonMesh grid cardinality

The typed reader now checks the physical coordinate count before constructing a
`PolygonMesh`. Previously it allocated an M×N array first, silently left missing
coordinates at zero, and checked that preallocated array's length in the spline
branch. Surplus coordinates could overwrite an earlier grid slot or eventually produce an index error. Malformed
counts now produce a contextual `InvalidDataException` in Debug and the existing
null-load result in Release, without terminating a Debug process.

## Schema and admitted profile

Autodesk's [POLYLINE group reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm)
separates M/N grid counts (71/72), surface densities (73/74), and surface type (75).
The [VERTEX group reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm)
distinguishes polygon mesh vertices, spline frame controls, and generated spline
vertices by bits 64, 16, and 8 respectively.

| Typed profile | Parent type / flags | Required grid records | Separate generated records |
| --- | --- | --- | --- |
| Ordinary polygon mesh | group 75 = 0; spline-fit bit clear | exactly M×N VERTEX records with group 70 = 64 | none |
| Quadratic or cubic surface | group 75 = 5 or 6; spline-fit bit set | exactly M×N VERTEX records with group 70 = 80 | group 70 = 72 |

Mixed or ambiguous vertex classifications, unsupported surface types (including
Bezier type 8), and disagreements between the surface type and spline-fit flag
are explicitly rejected. Generated sample counts do not substitute for the
control grid count. The reader accepts differing generated sample counts because
the existing typed model stores controls and regenerates samples on export; it
does not claim to preserve the source sample stream. Exported sample counts are
checked independently against the emitted surface densities.

M and N retain the existing library range **2 through 256**. This is a typed
model admission bound, not a claim that Autodesk limits the DXF wire format to
256. The checked product is at most 65,536. Signed 16-bit M/N values cannot
overflow an Int32 product; this change fixes missing/surplus grid validation and
declaration-driven allocation, not a newly discovered integer overflow.

Omitted density groups retain the model's default zero; explicit recovered low
density values follow the existing minimum of three generated samples.
POLYFACE header counts remain advisory and unchanged by this module.

## Export and model guard

The mutable coordinate array is checked for nonfinite values before writer
preprocessing changes layouts, registrations, handles, or stream bytes. The
`SmoothType` setter rejects unknown enum values without mutation. Sampling and
export also refuse control grids too small for this implementation's spline
basis: open dimensions require more controls than the degree; closed dimensions
require at least the degree. A caller may still stage such a grid while editing
its closure flags; it cannot sample or export it until the configuration is
valid. A complete grid can load even when its current degree/closure combination
cannot be sampled; it is refused at sampling/export until repaired. Source
nonfinite numbers are already rejected by the transport readers; the export
check addresses later mutation of the public coordinate array. These are typed
implementation constraints, separately tested from the read-side count contract.

## Evidence and boundaries

The producer fixtures in `tests/fixtures/polygonmesh-cardinality` were made with
**ezdxf 1.4.4** in all six supported DXF versions, in ASCII and binary. Smoothed
fixtures contain explicitly classified controls and illustrative sample packets;
they are schema evidence, not native CAD spline evaluation evidence.

Two native ordinary 3×4 grids are transplanted from pinned LibreDWG TS1 originals
already stored under `tests/fixtures/field-oracle`. Each carrier retains the
POLYLINE `20F`, its twelve VERTEX records, and its SEQEND. The sole packet edit
rebinds the parent's common owner from source BLOCK_RECORD `1F` to carrier `17`.
`native-manifest.json` records compressed source hashes, carrier hashes, and exact
physical coordinate order. The independent gate compares all fourteen packets
against the pinned original after that declared owner mapping.

The native corpus scan found two such ordinary grids in 66 parsed drawings out
of 67 candidates. It found no smoothed native polygon mesh. Consequently this
module makes no native smooth-surface qualification claim. It also does not
preserve native child handles, child XData, extension dictionaries, reactors, or
source sample identities. The earlier stored VERTEX module concerns a separate
ordinary Polyline3D profile.

`tools/verify_polygonmesh_cardinality.py` requires every module output, checks
physical grid counts and order, requires clean ezdxf audits, verifies native
packet provenance, and rejects twelve actual ASCII/binary corruptions. Tests
exercise malformed count/range/type cases, maximum product, default densities,
periodic spline minima, caller stream lifetime, export refusal before mutation,
and POLYFACE advisory counts. `DXF_TEST_FILTER=polygonmesh/` selects this module;
the default suite is unchanged.
