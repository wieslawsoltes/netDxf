# Imported source identities and numeric handle semantics

This increment distinguishes an actual retained DXF source object from a generated
default object that happens to receive the same numeric handle. It also makes
public handle lookup and typed graph operations agree on hexadecimal identity.
These are stored graph guarantees, not application evaluation or a claim that
every private DXF handle has a known schema.

## Imported typed references

The reader observes each physical record's common header once. It associates the
accepted object instance with that same record's common group 5 identity, or group
105 for DIMSTYLE. Handles in subclass payloads, application control groups,
XData, ignored sections, or discarded entities do not establish another object's
identity. Nested INSERT/ATTRIB and BLOCK/ENDBLK parsing retain separate record
associations. The documented SECTION entity spelling is distinguished from a
file-level SECTION delimiter.

Resolution requires both that accepted source instance and its current document
registration. DATATABLE object cells, common database owners, dictionary entries
and defaults, extension dictionaries, persistent reactors, IDBUFFER entries,
SORTENTSTABLE targets, and LAYER_INDEX owned buffers use this check. Numeric zero
continues to represent an absent reference in the fields that allow it.

Legacy conversions need explicit mappings. Managed named collections are admitted
only for their corresponding reserved named-dictionary entry and source owner.
The consumed layer-state extension dictionary is accepted only for its owning
LAYER table. This exception does not skip subsequent persistent-reactor checks.
Real retained symbol tables, records, entities, resources, and named collections
remain valid targets.

Regression inputs cover absent source objects, discarded records, records in an
ignored section, missing common identities, payload-only identities, generated
collection collisions, valid forward references, and case/leading-zero variants.
An independent frozen matrix checks 106 existing positive and negative cases;
additional same-record provenance and consumed-extension cases are tracked in the
batch qualification receipt. The public API throws parser exceptions in Debug
and returns null for these failed loads in Release; tests accept the documented
rejection form in each configuration and verify that caller streams remain open.

## Numeric handles in public lookup and graph operations

`DxfDocument.GetObjectByHandle` accepts one through sixteen hexadecimal digits.
Case and leading zeros denote the same numeric identity. Null, empty, malformed,
and over-width input returns null. Lookup of numeric zero retains the historical
document-object behavior, while semantic zero references in XRECORD and XData
are treated as absent by validation and cloning.

Writing preserves handle spelling held in the model. A mapped copy uses the
destination object's actual handle for a semantic reference, retains numeric-null
strings, and leaves arbitrary groups 320–329 untouched. The established typed
reader canonicalizes hexadecimal strings on load, including arbitrary handle
groups; these tests do not claim byte-for-byte lexical round trips through the
typed reader. Arbitrary data does not become a clone dependency merely because
its numeric value resembles an object handle.

The fifth mixed-module fixtures include zero-padded lowercase semantic XRECORD
references and independently verify the emitted source spelling and copied target
identities. Additional memory-stream cases cover lookup boundaries, null XData,
explicit external maps, and incoming-reference erasure guards. See the final
table-storage qualification receipt for the source pin, configuration counts,
and independent verifier results.
