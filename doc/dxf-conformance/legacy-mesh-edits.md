# Explicit legacy-mesh edits, signed faces and stored density fidelity

This increment follows PR #212. It covers the legacy POLYLINE-based PolygonMesh
and PolyfaceMesh types, not the modern MESH entity or full native CAD rendering.

## Explicit editing

`PolygonMesh.SetVertex(u, v, position)` retains its established grid mapping
`u + U*v` and out-of-range no-op behavior. For valid indices the replacement must
now be finite. Exact component bits, including the sign of zero, determine
whether the assignment changes geometry and clears the parent common proxy.
Identical assignments do not write the array. Other coordinates are not
revalidated: this existing API still permits incremental repair of a grid whose
raw array was changed. Retained record identity and metadata stay attached to
the same slot. Grid closure, density and smoothing setters also invalidate
parent graphics only when their stored fields change, after existing admission.

Three explicit methods are added to PolyfaceMesh:

```csharp
mesh.SetVertex(4, new Vector3(20, 30, 40));
mesh.SetFaceVertexIndex(0, 2, 8);       // signed, one-based vertex index
mesh.SetFaceEdgeVisibility(0, 0, false);
```

Polyface methods validate the current normal, complete geometry and active face
indices before mutation. A retained parent must be registered in its captured
source document/profile; a qualified detached clone must be adopted first.
Candidate face slots are copied and validated before the live slot changes.
They retain the coordinate array, face object, index array, VERTEX objects,
handles, owners, metadata and incoming reference targets. No handle is allocated.
Changed coordinate bits or stored signed face slots clear parent proxy graphics;
exact no-ops and rejected input preserve absent, empty and nonempty caches.

The established face model remains: one to four signed-short slots, signed
one-based indices, and zero terminating the active sequence. Ignored slots after
zero may contain other values; activating them requires the whole candidate to
validate. A negative active index hides the edge beginning at that corner.
Inactive corners reject visibility edits. Index -32768 may identify vertex32768
with a hidden edge, but making that edge visible rejects because +32768 cannot
fit a signed short. No magnitude overflow or silent wrapping is allowed.

Typed face loading exposes active indices. An authored trailing zero/ignored
tail is not promised typed round-trip array equality. Existing retained physical
slots, including a newly written zero, remain in the stored packet. The new API
does not expand a captured slot layout. Direct `Vertexes` and `VertexIndexes`
array changes remain compatible but unobserved: callers must manage cache and
record compatibility when bypassing these explicit methods.

## Ordinary polygon-mesh density values

Tests exposed an independent IO loss: both ordinary writer paths emitted zero
instead of stored DensityU/V, while ordinary retained loading discarded incoming
groups73/74 after the generic fitted-density normalization.

The ordinary reader now restores the raw signed-short hints, separately from
the existing fitted-surface normalization. Both ordinary writer paths emit those
stored values. Missing input fields default individually to zero; output still
materializes the fields, rather than preserving their absence or lexical spelling.
Hydration does not invalidate stored proxy graphics. Clones already copy these
fields directly. Public assignments still require3..201; source retention outside
that authoring range is not a new authoring or native-validity claim.

This applies only to ordinary unsmoothed grids. Fitted-surface sampling, minimum
precision, SURFU/SURFV defaults and fitted record handling remain unchanged.
Dormant hints do not change U/V cardinality or the number of active vertices.
Switching a retained ordinary sequence into a fitted layout remains subject to
the existing refusal/preflight constraints; this is not general conversion.

## Regression coverage and independent checks

The shared source registers **409 cases**, used by full conformance, ordinary
installed-package consumption and all eight exact-asset/runtime profiles:
156 edit/no-op cases,210 refusals,12 scalar/clone cases,5 edge/reference/state
cases,2 non-square grids,12 edit matrices and12 independently injected density
matrices. The existing twelve package-smoke scenarios remain twelve; these
additional assertions are not represented as additional historical scenarios.

Edit matrices cover six supported typed versions, both transports, four
placements, two mesh types, nine operations per type and three proxy states.
Source/output/opposite-transport resaves create36 drawings /7,776 parents.
Density matrices inject12 signed-short/absent-field profiles independently of the
new writer and produce36 drawings /432 parents. They include zero,1,2,negative
hints, signed-short extremes,201,202/203, ordinary values and omitted fields.
Density source files are text; both output transports and opposite resaves run.

All72 drawings /8,208 parents require exact coordinate bits, signed physical
face indices, U/V order, stable parent/child identities, owners, XData, flags,
proxy presence/bytes, following LINE, source preservation and stream lifetime.
Edit fixtures attach independently named metadata to every retained child.
Non-square3x4 grids separately exercise every logical slot and round-trip order.

The independent checker computes expected geometry, index signs and densities
from fixed input specifications, not recorded library results. It checks actual
physical packets before ezdxf normalization, exact file/record inventories,
version-specific92/160 proxy lengths, and ezdxf interpretation/audit. Cross-save
comparison excludes only separately verified editable scalar fields; identities,
owners, metadata and unselected packets are never normalized. Corruption controls
challenge every coordinate component, signed face fields, flags, density, proxy
state, child identity/owner and complete inventories. A structurally valid but
changed terminator identity must also fail between saves.

These are test definitions, not statements that a particular build passed.
Execution evidence and source hashes belong in the PR and retained run manifests.
Native AutoCAD open/AUDIT/save/reopen is not inferred from an independent reader.

## Qualification boundaries and references

Parent common-graphics invalidation is an explicit library editing policy. No
replacement graphics, private child-cache regeneration, arbitrary deep-change
tracking, transaction-wide callback rollback, native font/linetype/smoothing
rendering or projective transformation support is introduced. Existing transforms,
modern MESH, versions, workflow gates and the separate JavaScript port are unchanged.
Historical typed dialects, private FIELD/TABLE regeneration, dependency-complete
imports/cloning, general version conversion and full AutoCAD parity remain open.

Primary references:
- Autodesk POLYLINE fields, flags, counts and surface densities:
  https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm
- Autodesk VERTEX fields, signed polyface indices and edge visibility:
  https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm
