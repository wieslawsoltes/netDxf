# Atomic FIELD outcomes and literal text hosts

This opt-in extension publishes cached FIELD outcomes and selected text strings
in the same single-threaded transaction. It builds on the existing persistent
[FIELD result APIs](field-results.md) and can use the bounded
[standard evaluator](standard-field-evaluation.md). Existing cache-only APIs keep
their behavior: they do not acquire implicit entity-text side effects.

## API and association

```csharp
var evaluator = new DxfStandardFieldEvaluator(variableBindings);
int changedFieldPayloads = document.Objects.EvaluateFieldTreesAndUpdateTextHosts(
    selectedRoots, evaluator.EvaluateOrThrow);

// Explicit detached results use the same atomic preparation/publication path.
int changed = document.Objects.ApplyFieldResultsAndUpdateTextHosts(resultEdits);
```

`selectedRoots` must be complete FIELD ownership roots. `resultEdits` retain the
existing requirement to include every selected child's FIELD ancestors. The
return value counts changed FIELD payloads, **not** changed host strings. A host
can be corrected while returning zero when its cached FIELD outcomes already
match the request.

The accepted association is the actual ownership chain:

`host.ExtensionDictionary -> ACAD_FIELD dictionary -> TEXT root FIELD`

Both named slots must be present as hard-owner entries and all reciprocal owners
must match their actual registered objects. Name comparisons are case-insensitive;
dictionary fallback values and same-name foreign objects are not substitutes.
The implementation is original. The association is independently illustrated in
[Kean Walmsley's original AutoCAD example](https://keanw.com/2007/07/accessing-the-a-2.html).
The [Autodesk FIELD reference](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-51B921F2-16CA-4948-AC75-196198DD1796.htm)
describes the distinct code, child/dependency, cached-value and display packets.
No native evaluator or undocumented private expression is executed by discovering
this association.

Qualified hosts are TEXT, standalone MTEXT, ATTRIB and ATTDEF. A successful root
replaces the host's **whole value** with its explicitly supplied formatted text.
This is not substring substitution within an arbitrary host expression. A failed
root leaves its host unchanged, including when the failure explicitly supplies
a usable fallback cache. A host that requires visible error text must receive
an explicitly successful parent outcome chosen by the caller.

## Literal text and display invalidation

TEXT, ATTRIB and ATTDEF require single-line text without control characters.
MTEXT converts CR, LF and CRLF to paragraph controls and escapes literal braces
and backslashes. Native percent controls (`%%`), FIELD delimiters (`%<` and `>%`)
and Unicode-escape prefixes (`\U+`, case-insensitive) reject before publication.
Actual Unicode characters remain supported. The Unicode-escape rejection is
intentional: ordinary entity writers do not quote every backslash like the FIELD
cache writer, so passing those sequences through could change their meaning on
reload. This path does not silently promise literal rendering it cannot preserve.

Standalone MTEXT excludes both column parents and their linked continuation
entities. Reflowing columns requires a separate complete operation; checking only
`host.Columns` would miss linked followers. Host extents, width, height, insertion,
normal, alignment, style and colors remain unchanged. Updating an ATTDEF does not
synchronize existing INSERT attribute instances.

A changed host loses its common proxy graphics. A changed ATTRIB also clears the
owning INSERT proxy, which may contain the former attribute appearance. Unchanged
strings retain their proxies, even when other FIELD cache properties change.
Failed root outcomes do not clear host proxies. No display blocks, table geometry,
font metrics, native layout caches or evaluator checksums are regenerated.

## Preparation, callbacks and limits

Every root's host association, current string and proxy identity are captured
after graph/schema preflight and before any host evaluator runs. Existing FIELD
traversal, context checks, snapshots, failure outcomes, byte/text budgets and
reentry protections still apply. Hosts and owning INSERTs are checked again
before publication. A callback changing a selected host string, proxy, ownership
or association invalidates the operation. Its independent external changes are
not rolled back, but no library-prepared FIELD/host result is partially committed.

All FIELD candidates and escaped host strings are created before the first state
swap. Publication uses only the existing nonvirtual value setters and proxy-array
assignments; it invokes no user callbacks and allocates no handles or resources.
The earlier payload/evaluation/reference-membership snapshots remain unchanged.
A late invalid host or failed last-root callback cannot publish an earlier tree.
The common guard is usable after rejection, including caught nested API calls.

The escaped host string is bounded by the existing 1,048,576 UTF-16-unit limit.
Combined source host strings and combined generated host strings each have the
4,194,304-unit FIELD transaction limit. The independent existing FIELD-result
text and byte limits still apply as well. Binary FIELD caches can retain CR/LF;
ASCII save continues rejecting newline-bearing FIELD packets before bytes are
written even when the MTEXT host uses escaped paragraphs. Thread-concurrent
mutation, process termination and memory exhaustion are not transactional
recovery guarantees.

## Attribute ownership fixes found during validation

Attributes remain owner-held records rather than members of `AddedObjects`.
The public `DxfDocument.GetObjectByHandle` contract is unchanged. Database
registration validation now additionally recognizes an attribute only when its
actual INSERT is registered, its current attribute collection contains that exact
instance and the retained numeric identity resolves to it. Detached, removed,
foreign and colliding instances cannot impersonate a valid metadata owner.

Database object owners, common metadata targets and persistent reactors can now
resolve accepted owner-held source identities through the existing source-identity
proof. Physical duplicate/discarded-source checks remain enabled; generated
default objects are not substituted. Common extension dictionaries and reactors
on retained attributes participate in database validation and survive save/reload.
The former Debug assertion for a missing attribute tag now raises a FormatException
rather than terminating the process. Release retains its ordinary load-failure
wrapper. This is deterministic malformed-input rejection, not acceptance of an
invalid or omitted owner.

## Verification and reproducibility

The new C# suite has 282 cases: 48 source-profile/transport/host round trips,
120 atomic-failure cases, 40 result-policy cases, 18 admission cases and 24
attribute metadata/identity cases, plus 32 standard-evaluator integration cases.
It includes reentry, late exceptions/nulls,
explicit batches, host-only correction, failed outcomes, literal controls,
column/follower refusal, proxy invalidation and exact source identity. Every
prior test remains enabled. Debug and Release load rejection differ in exception
versus null reporting; the negative tests accept the documented wrapper result,
not a generic exception from a test helper.

`verify_field_text_hosts.py` requires exactly 48 before/after pairs (96 files).
The hosts are explicitly synthetic carriers around the original compact/modern
FIELD bodies. The two native producer-file hashes are checked; other version
carriers are not represented as additional native source drawings. The oracle
computes both changed FIELD packets and the selected literal host value itself,
then compares ordered complete physical records. The unselected FIELD tree,
other host, ownership dictionaries, entity geometry and resources must stay exact.
All 96 files are separately opened and audited with ezdxf without repairs.

Two existing writer artifacts are explicitly isolated from that comparison:
the previously qualified exact-empty ACAD_LAYERSTATES dictionary identity and,
for ATTRIB fixtures only, regenerated INSERT SEQEND identities. Sequence-end
normalization requires exactly two one-attribute INSERT sequences, reciprocal
attribute owners, exact empty SEQEND packets on the parent layer and **no incoming
semantic or arbitrary handles** to those records. Metadata, extra fields or a
referenced sequence end reject rather than being normalized. Physical position
and parent identity remain checked. HEADER time/seed and CLASS records are
outside the shared physical-record comparator.

The actual-output gate rejects 8,048 individual FIELD/host/proxy/record
corruptions through the same comparator. Eight separate oracle tests cover
literal encoding, proxy admission, both generated-sequence restrictions and
missing/extra output inventories. These reference tests are not substituted for
checking actual library-produced files.

```sh
DXF_TEST_FILTER=field-host/ dotnet run --project tests/netDxf.Conformance -c Debug
python -m unittest discover -s tests/field_hosts -p 'test_*.py'
python tools/verify_field_text_hosts.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

CI installs the pinned independent-reader dependencies before running the Python
reference suites on every configuration. The previous workflow invoked the
standard evaluator checker tests before installing ezdxf and therefore stopped
before C# compilation. No test is disabled by correcting this ordering.

## Remaining scope

This closes explicit cache-plus-literal-text publication for the qualified
association, not automatic document-event evaluation, arbitrary embedded native
FIELD syntax, cross-root private dependencies, structural FIELD authoring or
native checksum updates. General TABLE inheritance/duplicated-format/cache
synchronization, recursive imports, complete private schemas and source-document
version migration remain separate. Actual AutoCAD open/AUDIT/save/reopen,
installed-font measurements and native visual equivalence are not claimed.
