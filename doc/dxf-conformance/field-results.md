# Persistent FIELD results and atomic multi-tree evaluation

Loaded `DxfStoredField` objects preserve their ownership/reference graph. This
module adds immutable projections and explicit transactional updates of qualified
cached results, successful or failed. It does not execute every native evaluator,
author new FIELD structures, or remove existing cloning/erasure/version guards.

## Source evidence and projection

`DxfStoredField.Evaluation` exposes a complete recognized envelope: evaluation
and filing options; state/status/error metadata; ordered named evaluator data;
the cached null, Int32, finite-double or string scalar; raw format controls; and
both display strings. `DxfFieldDataValue` retains each named scalar packet and
its decoded key. Duplicate keys remain ordered, without inferred private meaning.

Compact and modern AcValue framing comes from two producer files in the
[FIELD manifest](../../tests/fixtures/field-oracle/manifest.json), with exact
source-byte and Git-blob hashes. The R2000 and R2018 records are used in the
existing [scoped carriers](stored-fields.md): their original ATTDEF hosts are
explicitly replaced with synthetic LINE hosts. Neither whole source drawing is
qualified by this module. R2004/R2007/R2010/R2013 cases are synthetic profile
carriers of those compact/modern grammars, not additional producer evidence or
whole-document conversion.

The [Autodesk FIELD reference](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-51B921F2-16CA-4948-AC75-196198DD1796.htm)
identifies leading evaluator/code/child/object fields, keyed data, cached values
and formatted-string length. Reused codes are interpreted within their actual
packet scope, not through global first-occurrence lookup. FIELD code chunks are
joined before Unicode decoding, including escapes split across groups 2 and 3.

Unknown value kinds, private extensions, missing/repeated fields and mismatched
display lengths leave `Evaluation` null without discarding `Payload`. AcValue
flags, units and format controls are retained, not silently reinterpreted.
A qualified parent does not make an unqualified child editable.

## Explicit result batches

`DxfFieldResult` contains an explicit scalar, formatted FIELD text and an optional
modern AcValue display string. The last defaults to the FIELD text and is ignored
by compact encoding. Strings are not coerced into numbers or executed. The host
supplies formatting rather than asking the library to guess private controls.

```csharp
var childResult = new DxfFieldResult(42, "42 units");
var parentResult = new DxfFieldResult("42 units", "42 units");
int changed = document.Objects.ApplyFieldResults(new[]
{
    child.Evaluation.WithResult(childResult),
    root.Evaluation.WithResult(parentResult)
});
```

Every request binds an actual current snapshot. Foreign, stale, repeated and
unqualified targets reject. Updating a child requires an explicit result for
every FIELD ancestor, so ancestors cannot silently retain successful stale
caches. This rule does not infer dependencies hidden in private evaluator data.

Successful results set Evaluated, HasCache and HasFormattedString, clear Modified,
preserve other state bits, set Success and clear the old error. These are the
published [state](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_FieldState.html)
and [status](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_FieldEvaluationStatus.html)
values. The caller's outcome is recorded; native AutoCAD execution is not implied.
Evaluator code, filing/evaluation options, named data, raw AcValue flags/units/
formats, dependency tags and common metadata remain fixed.

Scalar type changes among the four supported kinds are explicit. Equal scalars
retain existing packet tags, including optional empty-value padding. Doubles
preserve bitwise negative zero. Identical results with matching status/error
metadata preserve snapshots and lexical spelling. Edited display text uses group
301 and 250-UTF-16-unit group-9 continuations without splitting surrogate pairs.
Group 98 is decoded UTF-16 length, not bytes or Unicode code points. Literal
backslashes are escaped once; pre-R2007 non-ASCII text uses DXF Unicode escapes.

## Failed evaluation and explicit cache policy

An exception aborts the complete result transaction. A failed `DxfFieldResult`
is different: it is an intentional completed outcome that can be persisted.
`DxfFieldResultStatus` exposes Success (2), EvaluatorNotFound (4), SyntaxError (8),
InvalidCode (16), InvalidContext (32) and OtherError (64). Failure factories accept
one supported failure, not mixed/unknown masks or NotYetEvaluated. Error codes
are evaluator-defined signed integers; messages are bounded decoded text.
The ordinary constructor still produces Success, error zero and an empty message.

```csharp
// Retain the exact current cached scalar and display packets.
var retained = DxfFieldResult.Failure(input.Evaluation,
    DxfFieldResultStatus.EvaluatorNotFound, 7, "No recognized evaluator.");

// Record a failure and deliberately supply a replacement cache/display.
var fallback = DxfFieldResult.FailureWithValue(
    DxfFieldResultStatus.InvalidCode, 19, "Unsupported expression.",
    value: null, formattedText: "#ERR");
```

Autodesk's [evaluator contract](https://help.autodesk.com/cloudhelp/2027/ENU/OARX-RefGuide/files/OARX-RefGuide-AcFdFieldEvaluator__evaluate_AcDbField__int_AcDbDatabase__AcFdFieldResult_.html)
permits retaining cached values or substituting an error fallback. This API makes
that policy explicit; it does not invoke the native evaluator. A retained failure
is bound to its source snapshot. Applying it to another field, or wrapping an old
retained result in a newer snapshot's request, rejects before publication.

Retained failures preserve complete scalar/display packets and cache-presence
flags, including cases where no usable cache was originally present. Explicit
fallbacks establish cache/display presence. Both completed outcomes set Evaluated
and clear Modified while retaining other state bits; status/error code/message
change together. Repeating the same outcome is a no-op. Later successful results
clear failed status and error data. AcValue flags and evaluator checksums are
never silently rewritten to pretend the native evaluator processed the value.

## Child-first host evaluation and multiple roots

`EvaluateFieldTree` evaluates one complete ownership tree. `EvaluateFieldTrees`
accepts distinct complete roots and commits all selected trees together. Every
root is enumerated and disposed before callbacks; every tree's registration,
projection, context and depth is checked before evaluation starts. Root order
and stored child order determine child-first callbacks, once per identity.

```csharp
int changed = document.Objects.EvaluateFieldTrees(selectedRoots, input =>
{
    if (input.Children.Any(child => child.Status != DxfFieldResultStatus.Success))
        return DxfFieldResult.Failure(input.Evaluation,
            DxfFieldResultStatus.OtherError, 1, "A required child failed.");
    if (input.Field.EvaluatorId == "_text")
        return input.ComposeText();
    return DxfFieldResult.Failure(input.Evaluation,
        DxfFieldResultStatus.EvaluatorNotFound, 2, "No host evaluator.");
});
```

Callbacks receive original evaluation snapshots, live FIELD identities and
immutable detached child outcomes. Live caches remain unchanged throughout
all callbacks. A last-root exception, invalid result or encoded-string overflow
cannot leave an earlier tree committed. Empty forests invoke no callbacks.
Duplicate, foreign, child or null roots, invalid late trees and iterator failures
reject before evaluation. An explicit failure outcome is not a thrown exception.

`ComposeText` accepts only `_text` literals and `%<\_FldIdx N>%` slots. Repeated
occurrences reuse the child's result. Inserted text is literal and never reparsed,
so marker-like child content is inert. Unknown controls, malformed delimiters,
invalid indices and referenced failed children reject. The host can explicitly
propagate a parent failure or select its own fallback; the helper will not
silently turn a failed child's cached text into a successful parent result.

The default context is on-demand (32). Nonzero masks within 1–63 are admitted;
every selected field must enable at least one requested option bit. Disabled
fields reject the complete operation. Preview/plot-preview contexts, automatic
document event hooks and partial-subtree policies are not implemented. Forest
ordering follows ownership only, not arbitrary object references, private strings
or cross-root application expressions. The host must supply those semantics.
Existing numeric formula and unit utilities can be called explicitly by a host;
that does not implement general native FIELD/date/angle code or sheet-set access.

## Atomicity, bounds and persistence

All enumeration/disposal, callbacks, graph validation, registration/profile/
snapshot checks, encoding and candidate parsing finish before publication.
All result APIs share a reentry guard, including nested exceptions caught by
caller code. The guard resets after failure. Final publication uses state swaps
only: no handles/resources/objects/ownership links are allocated or removed.
Exact semantic handle tag objects, not merely numeric target values, survive.
Previous payloads, evaluator snapshots and reference memberships remain intact.

Limits are 4,096 result targets, 256 ownership levels and 4,194,304 combined
scalar/display/error-message UTF-16 units per transaction; each decoded/encoded
string and complete edited FIELD record is separately limited to 1,048,576 units
or tags. Limits apply across the forest, not per root. Tree traversal is iterative
and its depth is checked before callbacks. Arbitrary recursion inside host code
is outside that bound. Common metadata/XData count toward record limits, including
no-op requests. Invalid UTF-16, NUL and excessive escaped strings reject. Binary
retains CR/LF; ASCII save rejects newline-bearing strings before writing bytes.

Host TEXT/MTEXT/ATTRIB/TABLE display caches, FIELDLIST, private evaluator data and
checksums are not synchronized. Existing removal/clone/erasure/profile guards
remain active. Independent document changes performed by callbacks are not rolled
back. The API is single-threaded; concurrent mutation, process termination and
out-of-memory recovery are not transactional guarantees.

## Verification

The recovered suite covers scalar kinds, compact/modern framing, all six tested
profile carriers and both transports; child composition; explicit batches;
no-op/stale behavior; depth, size, callback and disposal boundaries; Unicode
chunking; disabled contexts; immutable metadata and unknown-packet rejection.
The continuation adds all five failed outcomes with retained-empty, populated
and explicit-fallback policies; multiple root ordering/preflight/rollback;
wrong/stale retained-cache rejection; failed-child composition; error text limits;
and successful recovery from failed states. Synthetic profile/depth/alternate
code fixtures are labelled as constructions, not new native producer evidence.

`verify_field_results.py` requires 48 before/after pairs (96 files). It derives
the two cache updates independently, checks before packets against native source
records and compares complete ordered physical records, including the unselected
FIELD tree, FIELDLIST, evaluator data, dictionaries and synthetic host entities.
`verify_field_failures.py` adds 180 before/after pairs (360 files). Its before
bodies must match pinned packets or independently computed populated caches.
Only specified outcome metadata and cache-policy changes are allowed. Every
actual FIELD tag, unrelated record and participating-object removal is challenged
using the same complete-record comparator as positive output.

Both gates require exact file inventories, source profiles and transports and
independently audit every drawing with ezdxf without repairs. Only the existing
writer's separately checked exact-empty ACAD_LAYERSTATES identity is normalized;
HEADER clock/seed values and CLASS records are outside these physical-record
comparisons. Model-only oracle challenges are not substituted for library output.

The interrupted draft's compilation failure was one nullable test assertion;
the correction preserved its runtime non-null check. Recovery-stage source tree
`b1d1f65b5b9a131547da2bc5a8181b27309f933a` passed 37,901 unique cases, including
353 result cases. Those results do not substitute for the subsequent failure/
forest implementation. Final source, execution and artifact digests are recorded
in PR #105. Existing regression and independent gates remain enabled.

```sh
DXF_TEST_FILTER=field-results/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_field_results.py artifacts/conformance
python tools/verify_field_failures.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance
```

## Remaining boundaries

This implements cached outcomes in existing qualified persistent FIELD graphs,
not structural FIELD authoring or a universal native evaluator. Native expression
parsing, date/angle FIELD controls, private data/checksum regeneration, external
dependency resolution, automatic event evaluation and host display synchronization
remain separate. Native applications can invalidate or recompute supplied caches
from retained code/data; actual AutoCAD open/AUDIT/save/reopen is not established.

Automatic TABLE precedence, duplicated formatting, coordinated inline/backing/
private cache regeneration, complete private/color schemas, recursive imports,
general document-version conversion and native font/visual qualification remain
unfinished. The historical PR95 matrix is not relabelled as complete by this guide.

## Opt-in host publication

The later [text-host transaction](field-text-hosts.md) adds explicit cache-plus-host
publication for recognized ACAD_FIELD/TEXT roots. The cache-only APIs above retain
their behavior. General host reflow, table caches and private evaluator metadata
are not automatically regenerated.
