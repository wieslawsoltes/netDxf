# Native DIMASSOC, UCS and TABLECONTENT coexistence

`SeventhMixedModuleTests.cs` and its graph/lifecycle companions combine stored UCS
base relationships, native DIMASSOC packets and native TABLECONTENT ownership.
The matrix contains eight cases and requires 20 output drawings: R2004 and R2018,
each in text and binary, at `original`, `released`, `cloned`, `erased` and `context`
stages. The focused [qualification receipt](seventh-mixed-qualification.json)
records exact source and executable hashes and the surrounding regression scope.

The R2004 carrier loads the complete pinned `sample_AC1018_ascii.dxf`, including
composite wrapper 13DF, native TABLECONTENT 747, TABLEGEOMETRY and the DATATABLE
ownership closure. R2018 loads the complete pinned `acad_table_with_blk_ref.dxf`,
including TABLECONTENT 116 and its TABLESTYLE/resource graph. The DIMASSOC
extractions supply 32 native application packets per profile under the explicit
fixture handle map. Only their external model-space owner is rebound when these
packets join the TABLE drawings. Layer/style resources and empty dimension
blocks are explicitly authored carriers; they do not qualify native dimension
layout or rendering.

An authored FIELD references the actual UCS, DIMASSOC, dimension, source geometry,
TABLECONTENT and TABLESTYLE objects. Repeated and null dependencies retain their
positions. The R2018 graph also references a SECTION, its owned settings, a SUN
and the existing TABLE entity. SECTION sources share the native geometry and
TABLECONTENT identities; the SUN's aliased extension XRECORD references its host,
itself and those same external objects. These authored links exercise identity
and ownership without claiming FIELD evaluation, snapping, section generation,
solar calculation or TABLE regeneration.

The lifecycle tests protect shared dependencies when one mutable UCS consumer is
released. Ordinary entity/resource removal, immutable-container cloning and
erasure reject without changing membership, the handle allocation seed or caller
removal callbacks. A serialized before/after comparison checks the full retained
graph. Its normalization is limited to writer-generated unregistered VERTEX,
SEQEND and empty legacy layer-state-dictionary identities; registered identities,
scalar packets and reference topology remain compared. The separate internal
VERTEX retention gap is not qualified by this normalization.

Supported local UCS, SECTION and SUN copies receive distinct identities; SECTION
self/settings references and SUN owner/self links remap while external native
targets remain shared. Tearing down the copies removes their owned objects and
retains the original graph. Foreign UCS adoption requires the actual destination
base identity even when the destination already contains the same UCS name.
Refused foreign immutable-container copying does not allocate destination handles
or leave a dictionary entry. Expected-return removal assertions are allowed to
fail directly, rather than being caught by the expected-exception checks.

The context inputs use padded lowercase references in FIELD, DIMASSOC and UCS.
They must meet at the exact physical source objects. Discarding the referenced
UCS record by giving it an invalid name rejects the load. Profile conversion must
reject before changing an existing caller-owned stream's contents or position,
and before reserving any handles.

Run the mandatory independent gate with:

```sh
python tools/verify_seventh_mixed.py artifacts/conformance
```

The gate requires every named output and the pinned native source hashes. It
compares 970 native object/geometry packets across the 20 drawings, including
complete retained TABLE bodies and exact DIMASSOC ownership/reactor attachments.
It checks cross-family references, CLASS definitions/counts, clone disjointness,
UCS origins and base relationships, SECTION fields and geometry appearance, SUN
extension aliases and physical teardown. Native geometry comparisons permit only
explicitly enumerated zero/default fields added by the existing typed codecs.
The opaque repeated POLYLINE/VERTEX association path is compared as an unchanged
stored association packet; its internal VERTEX identity is outside this matrix.

Forty deliberate mutations of actual parsed output must fail the same validators:
UCS base, FIELD native target, DIMASSOC geometry, TABLECONTENT style, copied-object
presence, native cell text, native geometry, TABLE position, SECTION self-reference,
SECTION height/appearance and SUN owner remapping. Every unmodified output also
passes the pinned ezdxf structural audit with zero errors or repairs. No native
AutoCAD execution or entire-drawing fidelity certification is claimed.
