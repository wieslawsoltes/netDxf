# Objects, references and editing

## Choose an editing boundary

| Model | Editing boundary |
|---|---|
| Typed entities and resources | Use the public validated operations and document-owned collections |
| Stored objects and retained child records | Edit only recognized packets through their supported editing APIs |
| Raw documents | Produce new snapshots with explicit tag, record, geometry or graph operations |

Public mutable lists and vector/array values remain for compatibility. Direct
mutation can bypass notifications or validation. An operation that preflights its
own inputs does not make arbitrary edits elsewhere in the document transactional.
Refresh derived snapshots after committing an edit.

## Identity and dependency closure

An imported reference must resolve to the accepted source object and its current
document registration. A generated default object that happens to reuse a missing
handle is not a substitute. Duplicate physical declarations, payload-only handles
and references to skipped records can make input invalid.

`DxfDocument.GetObjectByHandle` accepts one through sixteen hexadecimal digits;
case and leading zeros denote the same numeric identity. Malformed input returns
`null`. Arbitrary groups 320–329 do not become semantic dependencies just because
their values resemble object handles. Typed loading may canonicalize spelling;
use [raw preservation](io.md#preserve-unfamiliar-data) for exact original bytes.

Cloning, import and erasure must respect ownership, dictionary entries, reactors,
XData and other interpreted relationships. Unsupported dependency closure can
cause an operation to reject. Do not work around that by copying handles between
documents or deleting owners behind live references. The current behaviors are
covered by the [conformance suite](../../tests/netDxf.Conformance) and its
[independent graph checks](../../tools/verify_integrated_database.py).

## Stored and opaque data

Recognized stored objects expose bounded views of retained packets. Their resource
references and source-version restrictions remain meaningful even when the
visible edit is only a name or scalar. Opaque data can survive a save without
becoming safely editable. Acceptance of one producer's qualified frame does not
admit every private variant.

Stored editing APIs can reject stale snapshots, unsupported structure, invalid
resources and reentrant edits from caller enumerators. Consult the individual
API for the publication boundary: rejection of its own change does not promise
rollback of unrelated side effects performed by a callback or another thread.
TABLE-specific snapshots and publication are covered in
[FIELD and TABLE](tables-and-fields.md).

## Raw handles and object transactions

The [raw handle index](../../netDxf/IO/DxfRawHandleIndex.cs) distinguishes physical
declarations from reference categories. Dependency traversal follows the selected
interpreted categories, preserves source order and reports unresolved, ambiguous
and uninterpreted slots. A resolved selected closure does not certify every
private reference in a drawing.

[Handle remapping](../../netDxf/IO/DxfRawHandleOperations.cs) and
[object transactions](../../netDxf/IO/DxfRawObjectTransaction.cs) operate on raw
snapshots rather than rewriting the source in place. Respect their record
ownership, collision and unsupported-context checks. Use the corresponding
[handle-operation tests](../../tests/netDxf.Conformance/RawHandleOperationsTests.cs)
and [object-store tests](../../tests/netDxf.Conformance/RawObjectStoreTests.cs) for
executable examples. Historical field-level contracts remain in [history](history.md).
