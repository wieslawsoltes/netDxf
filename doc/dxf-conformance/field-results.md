# Explicit FIELD result persistence and child-first evaluation

Loaded `DxfStoredField` objects already preserve their persistent ownership and
reference graph. This increment adds immutable projections of qualified cached
results and explicit, atomic updates of those caches. It is not an automatic
implementation of every native FIELD evaluator, and it does not change the
existing source-bound cloning, erasure or version-conversion guards.

## Stored evaluation and cache projections

`DxfStoredField.Evaluation` is a `DxfFieldEvaluationSnapshot`, or null when the
complete evaluation/cache envelope is not recognized. It exposes evaluation and
filing options, state/status/error metadata, ordered named evaluator data, the
cached scalar, format controls and both display strings. `DxfFieldDataValue`
retains each named scalar packet and its decoded key. Duplicate keys remain in
order without inferred private meanings.

The projection supports null, Int32, finite double and decoded string values.
It recognizes the compact cache shape in the pinned R2000 input and the modern
AcValue envelope in the pinned R2018 input. AcValue flags, units and format
controls are stored values, not newly interpreted evaluator semantics. Other
value kinds, private extensions, missing/duplicate fields and mismatched display
lengths leave `Evaluation` null; the existing immutable `Payload` remains intact.
A qualified parent does not make an unqualified child editable.

The source evidence is the two producer files in the [FIELD manifest](../../tests/fixtures/field-oracle/manifest.json),
with exact source-byte and Git-blob hashes. Their FIELD/owner records are used
in the existing scoped carriers, whose two source ATTDEF hosts are explicitly
replaced with synthetic LINE hosts. Full source drawings are not claimed to be
qualified. R2004, R2007, R2010 and R2013 tests are explicitly constructed carriers
of the corresponding compact/modern grammar, not additional native producer
observations or whole-document version conversion.

The [Autodesk FIELD DXF reference](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-51B921F2-16CA-4948-AC75-196198DD1796.htm)
identifies evaluator/code/child/object fields, evaluator data, cached value and
formatted-string length. Actual modern repeated-code framing comes from the
pinned producer packets. The implementation does not identify repeated groups
by a global first-occurrence search. Code chunks remain joined by the existing
reader before decoding; valid Unicode escapes can span group 2/group 3 boundaries.

## Explicit cached-result transactions

`DxfFieldResult` contains an explicit scalar, a formatted FIELD string and an
optional modern AcValue display string. The latter defaults to the formatted
string and is ignored by compact cache encoding. Strings are not executed or
coerced into numbers. Both strings are supplied by the caller, not guessed from
private format controls.

```csharp
DxfStoredField root = /* actual registered ownership root */;
DxfStoredField child = root.Children[0];
var childResult = new DxfFieldResult(42, "42 units");
var parentResult = new DxfFieldResult("42 units", "42 units");

int changed = document.Objects.ApplyFieldResults(new[]
{
    child.Evaluation.WithResult(childResult),
    root.Evaluation.WithResult(parentResult)
});
```

Requests identify actual current snapshots, not a lookup by handle or evaluator
name. Foreign, stale, repeated or unqualified targets reject. Updating a child
requires explicit results for every FIELD ancestor, preventing silent retention
of a stale successful ancestor cache. This does not infer dependencies hidden
in private evaluator data or unrelated object-reference expressions.

An explicit successful result sets Evaluated, HasCache and HasFormattedString,
clears Modified, preserves all other stored state bits, sets Success status and
clears the old error code/message. The public [state](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_FieldState.html)
and [status](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_FieldEvaluationStatus.html)
values supply these specific meanings. This records the caller's evaluation
result; it does not assert that AutoCAD evaluated the code. Filing options,
evaluation options, code, named evaluator data, raw AcValue flags/units/format
controls, dependencies and common metadata remain unchanged.

Scalar type changes among the four supported cache kinds are explicit. An
identical scalar keeps its existing packet, including an optional empty-value
padding tag. Bitwise negative zero is preserved for doubles. Identical results
with already-successful state keep the current snapshot objects and lexical
spelling. Edited display strings use group 301 and 250-code-unit group-9
continuations, without splitting UTF-16 surrogate pairs. Group 98 is the decoded
UTF-16 length, not a byte count or Unicode code-point count. Literal backslashes
are escaped once; pre-R2007 non-ASCII characters use UTF-16 DXF escapes.

Invalid UTF-16, NUL and oversized decoded/encoded text reject. Binary output
retains CR/LF, while text output follows the existing before-write rejection
policy. Common metadata/XData count toward the complete record budget. These
rules do not normalize unedited private payloads.

## Explicit host evaluation of an owned tree

`EvaluateFieldTree` validates and snapshots a complete ownership root before
calling the host evaluator once for each field, children before parents. Its
input supplies the original evaluation snapshot, the actual live field identity
and detached evaluated child results in stored order. Live FIELD caches remain
unchanged throughout every callback.

```csharp
int changed = document.Objects.EvaluateFieldTree(root, input =>
{
    if (input.Field.EvaluatorId == "_text")
        return input.ComposeText();

    // The host must explicitly recognize and evaluate its supported code.
    if (ReferenceEquals(input.Field, knownApplicationField))
        return new DxfFieldResult(42, "42 units");

    throw new NotSupportedException("No host evaluator for this FIELD.");
});
```

The existing numeric `DxfTableFormula` or unit-formatting utilities may be called
inside a host callback for explicitly selected application code. Doing so does
not teach the library to parse arbitrary Autodesk FIELD expressions, resolve
external sheet sets, or infer date/angle units. The library never automatically
executes evaluator IDs, scripts, reflection, files, services or native plugins.

`ComposeText` handles only the exact `_text` evaluator with literal text and
`%<\_FldIdx N>%` child references. Repeated child occurrences reuse the already
evaluated result, rather than reevaluating the child. Inserted child text is
appended literally and is never scanned again: field-like delimiters in that
text remain inert. Unknown controls, missing/negative/overlong/out-of-range
indices and malformed delimiters reject. The helper does not interpret MTEXT
formatting, general nested FIELD syntax or private evaluator controls.

The default evaluation context is on-demand (32). Explicit nonzero public
context masks within 1–63 are accepted, and every selected FIELD must enable
at least one requested bit in its stored evaluation options. Disabled fields
reject the complete request rather than silently using an old child result.
Preview/plot-preview contexts, automatic document event evaluation and partial
subtree policies are not implemented by this API.

## Atomicity, limits and reference preservation

All request enumeration and disposal, callbacks, source graph validation,
registration/profile/snapshot checks, encoding and candidate parsing complete
before publication. A late parent failure cannot publish an earlier child.
Both result APIs share reentry protection; a nested request invalidates the
outer request even if the callback catches its exception. The guard resets
after failure. Old payloads, evaluation snapshots and dependency memberships
remain unchanged.

The final publication phase consists only of state swaps. No handles, objects,
resources or ownership links are allocated or removed. Exact semantic handle
**tag objects** must be retained in each replacement packet, not merely their
numeric values. Source resource-removal protections remain in force after
save/reload. The whole operation is single-threaded; concurrent mutation,
process termination and out-of-memory recovery are not transactional guarantees.
Independent document changes performed by caller callbacks are not rolled back.

Limits are 4,096 result targets per transaction, 256 ownership levels per tree,
4,194,304 combined result-string UTF-16 units, 1,048,576 decoded/encoded units
per string and 1,048,576 tags per edited FIELD including common metadata. Tree
traversal is iterative. The depth bound is checked for the entire tree before
callbacks run; arbitrary recursion inside a caller callback is outside it.

## Verification

The C# suites cover compact and modern packets, all six tested profile carriers,
both output transports, four result kinds, child-first composition, atomic
explicit batches, no-op and stale snapshots, saved/reloaded dependency guards,
Unicode chunk boundaries, disabled contexts, callback/disposal/reentry failures,
limits, retained metadata and rejection of unknown cache variants. Deep-tree
fixtures and alternate `_text` codes are explicitly synthetic constructions.
Existing FIELD storage and graph tests remain enabled.

`verify_field_results.py` requires exactly 48 before/after pairs (96 files).
It independently derives the two requested cache updates, first checks each
before FIELD packet against the native source grammar carrier, and compares
complete ordered physical records. The unselected FIELD tree, FIELDLIST,
evaluator code/data, owner dictionaries, host entities and resource records
must remain exact. Only the existing writer's independently checked exact-empty
ACAD_LAYERSTATES dictionary identity is normalized; HEADER clock/seed values
and CLASS records are outside that physical-record comparison.

Every actual tag in both changed FIELD records is independently corrupted,
each unrelated record is changed, and participating fields are removed as
negative controls. The same whole-record comparator validates positive and
negative data. All 96 files are opened and audited using ezdxf, with source
profile/transport and exact fixture inventories checked. These file/graph
checks are not a native evaluator or rendering certificate. Final executed
counts, source digests and CI artifacts are recorded in PR #105.

```sh
DXF_TEST_FILTER=field-results/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_field_results.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

## Remaining boundaries

This implements persistent **cached results in existing qualified FIELD graphs**,
not public structural FIELD authoring or a complete native evaluator. Native
field-code parsing, private evaluator-data updates/checksums, external dependency
resolution, failure-state publication, automatic event-driven evaluation and
host TEXT/MTEXT/ATTRIB/TABLE display synchronization remain distinct work. A
native application may invalidate or recompute supplied caches from retained
code/data; native open/AUDIT/save/reopen behavior is not asserted here.

Automatic TABLE style precedence, duplicated formatting, coordinated regeneration
of all inline/backing/private caches, complete color/private-schema interpretation,
recursive dependency import, general document-version conversion and installed-font
or native AutoCAD qualification remain unfinished. The historical PR95 matrix
is not relabelled as complete; this guide adds the narrower current contract.
