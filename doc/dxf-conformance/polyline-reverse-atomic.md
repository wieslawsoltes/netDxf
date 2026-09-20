# Atomic typed polyline reversal and proxy invalidation

`Polyline2D.Reverse()` now validates all lightweight vertices before changing
list order. Previously the initial validation only handled retained legacy
POLYLINE records. A null lightweight vertex could therefore fail after the
list had already been reversed; invalid stored widths could fail after some
segment attributes were changed. Shared vertex objects could be overwritten
while still being used as another segment's input. Reversal also retained
parent proxy graphics despite changed direction and outgoing segment data.

The method now rejects null vertices, nonfinite positions/bulges, invalid
widths, and repeated references before reordering or changing any attributes.
Reference checks do not invoke user-defined Equals or GetHashCode. Distinct
objects at equal positions are supported. The alias restriction is intentional:
a single shared object cannot hold different per-occurrence outgoing values
without losing point identity. Validation is linear and uses O(n) temporary
reference storage; no speedup or unlimited-memory guarantee is claimed.

Valid reversal retains the existing mapping: point objects and IDs reverse,
outgoing bulges change sign and move to the opposite endpoint, and outgoing
start/end widths exchange along with their optional presence. Retained legacy
VERTEX records reverse with their points and parent width defaults exchange.
Open polylines' unused final attributes follow the existing reversible cyclic
mapping; they are not discarded. Two reversals recover geometry components and
presence, not previously invalidated proxies. Zero/one-vertex valid definitions
remain no-ops with their proxy bytes intact. All n>=2 reversals clear the parent
proxy, even if coincident geometry makes a directional change visually invisible.

Public property setters and geometry conversion are unchanged. Validation and
reversal assume no concurrent mutation of the exposed list or vertices. This
is not general transactional editing, private-cache regeneration, or notification
for arbitrary direct vertex assignments. Undefined/nonfinite values formerly
accepted by lightweight Reverse now deliberately reject.

## Verification

The new harness checks detached/owned models, closed/open curves, optional
widths, rejected late state, duplicate object references, derived equality
callbacks, bit preservation, degenerate no-ops, legacy identity/default-width
mapping, and modern text/binary round trips. Original test identities and all
independent verifiers remain enabled. Hosted execution results are recorded in
the task PR; no local C# run is claimed without a local SDK.

The independent checker regenerates exact reversed coordinates, bulges and
width presence, confirms no stale proxy packets, and uses ezdxf arc geometry
to compare reversed centerline samples and width progression. Its synthetic
fixtures are not native AutoCAD open/AUDIT/save/reopen or renderer evidence.

Autodesk's [LWPOLYLINE schema](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
defines vertex coordinates, widths, bulges and flags. Atomicity, alias admission,
cache invalidation and exception behavior above are library contracts.

Historical typed loading, pre-R11 formats, complete private FIELD/TABLE/cache
regeneration, dependency-complete imports, general version conversion and native
font/visual qualification remain separate work.

## Earlier common-data test expectation

The first hosted run exposed one conflicting baseline expectation: the common-
data API test required proxy graphics to survive two reversals. It now checks
that each traversal change invalidates that cache, that double reversal restores
vertex identities without resurrecting a cache, and that an independent clone
retains the original proxy bytes. The case remains registered, and its original
byte-array isolation, LINE transformation and rejected-setter tests are retained.
All 89 new focused cases passed on that first run; the whole suite correctly
failed until this expectation was updated. Final qualification requires a fresh
full run, not removal or filtering of the old test.
