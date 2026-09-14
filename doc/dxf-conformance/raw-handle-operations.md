# Raw reference traversal and simultaneous handle remapping

> Merged as [PR #72](https://github.com/wieslawsoltes/netDxf/pull/72). Final-head [CI 34863629586](https://github.com/wieslawsoltes/netDxf/actions/runs/34863629586) passed Linux/Windows Debug/Release, actual netstandard2.0 builds, ledger checks and the Linux source audit; the retained Linux Debug report has **16,315 passed / zero failed**. The evidence section below retains the original standalone local experiment, not the later accumulated suite count. See the [merged execution checkpoint](checkpoint-merged-2026-09-14.md) for source hashes and qualification limits.

This feature depends on `DxfRawHandleIndex`, not on typed-model handle allocation.
Both APIs work on one immutable raw snapshot and never rewrite it in place.

## Explicit reference traversal

`GetDependencyClosure(roots, traversal, cancellationToken)` follows selected exposed
reference categories using an iterative queue. Hard pointers and hard ownership are
the default; callers may include soft links, common parent owners, reactors, XData
or HEADER references. Arbitrary and opaque handles are not followed. Duplicate roots
coalesce, cycles terminate, foreign records reject, and root enumeration is bounded.
Results retain source order and expose unresolved, ambiguous and uninterpreted slots.
`AreSelectedReferencesResolved` concerns ONLY the selected interpreted categories.
It is not proof of complete semantic closure.

This is outgoing graph reachability, NOT independent drawing extraction, reverse
ownership traversal, dependency-closed cross-document import, or reconstruction of
BLOCK/POLYLINE lexical aggregates. Class-specific and hidden application references
need explicit schemas.

## Collision-safe remapping

`RemapHandles(mapping, cancellationToken)` renames unique nonzero identities and
interpreted exposed references simultaneously. Case/leading-zero aliases share a
numeric key. Swaps/permutations are permitted; normalized duplicate keys, missing or
ambiguous sources, duplicate targets and collisions with unchanged identities reject.
A new target may not capture a previously dangling interpreted reference. Exposed
opaque slots affected as source OR target require an application-specific contract
and reject. Arbitrary 320–329 values deliberately stay unchanged.

Preflight rejects ambiguous/null/multiple identities and malformed common control
framing. Existing singular `$HANDSEED` is advanced above the resulting maximum identity
when necessary; overflow or repeated seeds reject. Missing seeds are not invented.
Numeric no-ops return the original snapshot, retaining original bytes. All unaffected
tags retain object identity and order; edited output normalizes only its usual lexical
spelling. No downgrade, execution of application data or implicit dependency repair.
References hidden in strings/binary values remain outside this contract, so this API
must not be advertised as universally safe arbitrary-schema drawing renumbering.

## Executed evidence

Base: merged PR #68 plus the separately tested handle-index increment. 80 operation
cases plus 160 index cases produce a full **15,832 passed / zero failed** in each
local signed-library .NET 8 configuration. Coverage includes all nine raw profiles,
both transports, swaps, no-ops, preserved arbitrary/opaque slots, seed repair,
collision/alias/dangling-capture rejection, cancellation, stale roots and cycles.
Twelve actual typed drawings have every indexed identity remapped and are then
loaded through the typed reader after a transport change. Their initial text fixture
comments were explicitly removed before binary conversion; codec validation was not
relaxed. The independent verifier checks these 12 files for exact geometry, remapped
identities, safe seed and zero ezdxf audit errors/repairs in each configuration.
The verifier checks HANDSEED against the independently decoded authored tag identities,
not ezdxf's augmented object database: that loader creates optional defaults starting
at the original seed without immediately rewriting the HEADER value. This observed
loader behavior is not reported as a production remapping error or hidden by AUDIT.

This is new API validation, not a fabricated baseline red run. Windows/SDK/netstandard CI was not executed in that original experiment. The merged PR subsequently passed those gates; native AutoCAD remains unexecuted. The synthetic raw test records do not assert
historical legality in every profile.

```csharp
var index = DxfRawHandleIndex.Create(rawDocument);
var closure = index.GetDependencyClosure(new[] { record },
    DxfRawReferenceTraversal.HardPointers | DxfRawReferenceTraversal.HardOwnership);
var changed = index.RemapHandles(new Dictionary<string, string>
{
    ["10"] = "20", ["20"] = "10" // simultaneous swap
});
```

Primary source: Autodesk's numerical group-code reference and common entity control
group table; linked in `raw-handle-index.md`. These sources distinguish arbitrary,
soft/hard pointer, ownership, reactor and extended-data handles.

## Embedded-object follow-up

Merged [PR #75](raw-embedded-handle-context.md) keeps the recognized group-101 embedded-object tail opaque through the record boundary. Its private fields, including 100/102/1001-looking values, must not become enclosing identities or references. This is conservative exposure/remapping safety, not a private class schema.
