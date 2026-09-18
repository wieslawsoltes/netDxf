# Atomic MESH and 3DFACE affine transforms

This task follows merged C# baseline `9f302ed680cb3dba682b6d12d52d321d0109a139`.
It changes `Mesh.TransformBy` and `Face3D.TransformBy`, not the JavaScript port.

## Corrections and contract

The previous methods mutated vertices before validating later vertices and the
auxiliary normal. They accepted projective Matrix4 input after discarding its
bottom row, retained stale proxy graphics, and lost representable endpoint
cancellations to intermediate overflow.

Both methods now prepare every vertex and the auxiliary normal before changing
any entity field. Finite input is required even for identity operations. Matrix4
requires an exact `(0,0,0,1)` bottom row. Stored auxiliary normals require finite
unit components with squared length within `2e-15` of one. Publication bypasses
user-overridden Normal accessors. Rejected operations preserve geometry, proxy
bytes, metadata and source identities. Identity and numerically unchanged
operations retain existing components and proxies; positive/negative zeros
compare equal for the no-change decision.

WCS vertices use the exact dyadic arithmetic already qualified for RAY/XLINE:
each affine coordinate is rounded once to nearest binary64, ties to even. Final
coordinate overflow or a nonzero coordinate rounding to zero rejects. Auxiliary
directions use exponent-scaled normalization; a nonzero component underflow
rejects. A zero image retains the old auxiliary normal. Translation preserves
its exact components. These rules are explicit library admission policies, not
claims that AutoCAD uses the same algorithm or rejection threshold.

Singular affine maps can collapse a mesh or face; this is allowed. Vertex
ordering, 3DFACE invisible-edge flags, all MESH list/element identities, face and
edge indices, subdivision and crease metadata remain unchanged. This does not
validate mesh topology, evaluate subdivision, infer a face normal from MESH, or
regenerate a rendering. The independent existing MESH save preflight still
validates mutable topology. Concurrent caller mutation is unsupported.

Changed geometry clears common proxy graphics. It does not rewrite extension
dictionaries, reactors, application-defined XData geometry, or private caches.
The shared exact arithmetic and a prepared vertex array impose CPU/allocation
cost; large-model performance is not qualified by this task.

## Evidence

The same 977 focused cases pass 82 before and 977 after the correction. They
exercise both types and transform APIs, nine transform scenarios, two normals,
all matrix/translation elements, malformed and late-overflow input, exact
cancellation, proxy handling, source identity, and a throwing virtual accessor.

`verify_vertex_affine.py` independently computes expected WCS coordinates using
Python Fraction. It compares complete selected ordered entity packets except
handle/owner values, checks identity framing, and audits every drawing with
ezdxf. It qualifies 648 drawings / 2,592 vertices, rejecting 40,466 actual-packet
and missing/extra-inventory corruptions with zero graph errors or repairs.
3DFACE covers R2000/R2004/R2007/R2010/R2013/R2018 in text/binary; MESH remains
R2010/R2013/R2018. None of these tests adds historical typed writer support.

```sh
DXF_TEST_FILTER=vertex-affine/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_vertex_affine.py artifacts/conformance
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
```

Complete-suite, platform and netstandard2.0 results are recorded in the task PR
against the executed head. Synthetic numerical fixtures and independent graph
audits are not native AutoCAD open/AUDIT/save/reopen or visual qualification.

## Primary format references

- [Autodesk MESH DXF](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm)
- [Autodesk 3DFACE DXF](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-747865D5-51F0-45F2-BEFE-9572DBC5B151.htm)

The references establish WCS corner storage, repeated triangular corners, edge
flags and mesh topology/crease fields. Exact arithmetic, atomic mutation and
auxiliary-normal policies are library contracts. Private FIELD/TABLE caches,
complete resource imports, historical typed dialects, general version conversion,
other entity methods and native AutoCAD equivalence remain separate work.
