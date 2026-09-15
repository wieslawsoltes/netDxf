# Stored references during topology edits and affine HATCH transforms

`EighthMixedModuleTests.cs` combines the qualified SUNSTUDY, VIEW live-section,
TABLEGEOMETRY, retained Polyline3D and HATCH transform subsets. Eight cases cover
R2013 and R2018 in text and binary. Four pre-edit snapshots and four edited
outputs support independent comparisons of complete retained records.

The SUNSTUDY packet comes from the pinned IxMilia producer fixture through the
explicit [typed carrier adaptations](stored-sunstudy.md). Its complete body and
four actual dependency identities remain unchanged. The VIEW-to-SECTION link,
SECTION geometry, TABLEGEOMETRY packet and its VERTEX pointer are authored test
data. This mixed graph does not claim an unchanged native producer drawing.

A TABLEGEOMETRY cell references a newly inserted, subsequently reloaded VERTEX.
Removing that vertex must reject without allocating a handle. Moving it retains
the same object identity and coordinates, so the stored reference follows the
record. Inserting and removing an unreferenced temporary vertex retires its
registration; all other VERTEX records and SEQEND remain unchanged. The
independent comparison checks complete packets against their pre-edit order,
including identities, coordinates, owner and layer fields.

The SUNSTUDY reference prevents removal of its actual VIEW. That VIEW's live
section reference prevents removal of its SECTION. A second VIEW consumer keeps
the SECTION protected when the first reference is cleared. Releasing the copy's
reference permits removal of that copy while preserving the original chain.

The same drawing contains a solid HATCH with a stored periodic spline, a false
rational flag and a nonunit negative weight. An affine shear and scale changes
control positions and the closing line while preserving flags, knots and
weights. A collapsed-plane transform rejects before changing path identities,
coordinates, either stored-object payload, their references, object membership
or the handle seed. Saving the document after that refusal remains supported.
This qualifies stored transformation behavior, not periodic curve evaluation.

Run the mandatory independent gate with:

```sh
python tools/verify_eighth_mixed.py artifacts/conformance
```

The gate requires the eight named files, verifies the complete SUNSTUDY and
TABLEGEOMETRY bodies, exact retained VERTEX/SEQEND packets, the entire SECTION
record, the VIEW link and transformed HATCH data, including plane, seed and count
declarations. It applies 72 deliberate
mutations to actual parsed output, including coordinated VERTEX-and-pointer
rewrites that would otherwise preserve graph connectivity. Unchanged outputs
also require an ezdxf audit with zero errors and repairs. Native AutoCAD
execution, table layout, section generation and solar evaluation remain outside
this matrix.
