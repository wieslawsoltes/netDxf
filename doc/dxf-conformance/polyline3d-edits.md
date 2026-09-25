# Explicit 3D-polyline editing and parent graphics coherence

This increment follows the qualified INSERT/SEQEND work in #211. It addresses
`Polyline3D` edits which previously changed stored geometry or flags while
retaining the old common proxy graphics. It does not change DXF version support,
curve evaluation, affine transforms, VERTEX serialization or sequence ownership.

## Contract

`SetVertex(int index, Vector3 position)` replaces one coordinate while retaining
its VERTEX record object, handle, owner, references and metadata. It validates
both the replacement and the complete existing coordinate list before writing.
Retained sequences use the same existing registration, source-version,
unsmoothed-mode, count and capacity checks as the topology methods. No handle is
allocated. A detached retained clone must first be adopted by a compatible
registered document. Nonfinite values and invalid indices reject before mutation.

```csharp
Polyline3D path = /* an authored path or a compatible loaded path */;
path.SetVertex(1, new Vector3(20, 30, 40));
```

A change in any coordinate's binary64 bits clears the parent proxy. This includes
changing the sign of zero; no geometric epsilon is used. Reassigning identical
bits leaves an absent, explicitly empty or nonempty cache unchanged. Existing
invalid retained state still rejects even for an otherwise identical assignment.

Successful insertion, removal, a move to a different index and reversal of at
least two vertices also clear parent graphics. Coordinates and retained records
continue to move together, and existing protected-removal checks remain. These
nontrivial topology edits invalidate conservatively even when points coincide;
this is not an attempt to prove rendered equivalence. Equal-index moves validate
then preserve the cache. Reversing fewer than two points remains a no-op.

Changes to `IsClosed`, `LinetypeGeneration` and `SmoothType` clear the cache only
when the stored flags or smoothing type change. Existing admission is retained,
including rejection of `BezierSurface`. A smoothing change is not a new promise
that a retained sequence can be serialized with a different smoothing layout.
The internal hydration flags and the late reader attachment of common data are
unchanged. Valid stored graphics therefore survive ordinary loading and cloning.
No replacement proxy is fabricated.

The mutable `Vertexes` list remains source-compatible. Direct `List<Vector3>`
changes cannot notify the parent, so clients using that escape hatch must manage
cache invalidation and retained-record compatibility themselves. This increment
does not observe arbitrary deep metadata changes or revise private graphics in
individual VERTEX/SEQEND payloads. Cache coherence here is entity-local, not
whole-document rollback or callback transactionality.

## Regression and independent qualification

The shared source defines **177 cases**: 48 changes, 30 no-ops, 72 rejected edits,
15 grouped empty/singleton, scalar-bit, clone, incoming-reference and retained-state
cases, and 12 version/transport matrices. It is linked into full conformance,
the ordinary installed-package consumer and all eight exact-package/runtime
profiles. Grouped cases contain multiple assertions; they are not counted as
additional original test identities. Existing assertions and gates stay intact.

Each of the six typed versions (2000, 2004, 2007, 2010, 2013 and 2018) is tested
with both transports. Four placements, three cache states and eleven edit/no-op
operations produce 132 parent entities per document. Source, edited output and
opposite-transport resave produce **36 drawings / 4,752 POLYLINE parent records**.
Checks cover exact coordinate bits, ordered VERTEX identities, parent/SEQEND
handles and ownership, XData, placement, flags, cache state, source bytes, stream
lifetime, registration and following LINE geometry.

The independent Python checker computes geometry from fixed inputs rather than
captured netDxf output. It checks physical common proxy packets, including the
writer's existing group92-before-2013/group160-from-2013 policy. It verifies exact
cross-save identities without normalization, new versus retired vertex handles,
ordered coordinates, sequence framing, and ezdxf interpretation with zero
independent graph errors/repairs. Missing/extra files, stale or missing graphics,
changed flags, one-bit coordinate errors, sequence corruption and altered
otherwise valid terminator identities must reject.

Case and drawing counts above describe test definitions. Constructed checker
self-tests are not C# execution. Actual hosted results, source hashes and package
qualification must be recorded on the PR before merge; no native AutoCAD result
is inferred from this test design.

## Primary references and remaining boundaries

Autodesk documents POLYLINE flags/smoothing and the separately stored common
proxy bytes. Clearing stale proxy bytes on explicit edits is this library's
policy, not a claim of native rendering equivalence.

- Autodesk POLYLINE group codes:
  https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm
- Autodesk common entity group codes:
  https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm

Historical typed dialects, native AutoCAD open/AUDIT/save/reopen, complete
FIELD/TABLE/private cache regeneration, dependency-complete imports and cloning,
general version conversion, native smoothing/linetype display and arbitrary
raw-list edits remain unqualified. JavaScript PR #98 is separate. This increment
does not establish full AutoCAD parity.
