# Source-bound SECTION_MANAGER lifecycle

Executable checkpoint: `9cf0351f99f5c6ba50f27506f10790b57d1c650e`.
Executable tree: `6754231cc922d252c5c4f3022cb4f3ba5ad04a87`.
C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This implements the original-path `DxfStoredSectionManager` and
`DxfObjectDatabase.SectionManager` models. It is an in-memory registered lifecycle,
not a typed DXF reader/writer, general profile converter or section-geometry
regenerator. The original C# implementation, original tests and shared fixtures
are unchanged.

## Creation, membership and erasure

`document.Objects.CreateSectionManager(sections, requiresFullUpdate)` consumes
an ordered sequence of actual registered SECTION objects in the same document.
Repeated identities are retained, with the original limit of 65,536 entries.
Creation is supported for the source's R2007 through R2018 typed profiles. It
preserves the named-object root's flags and attaches a soft `ACAD_SECTION_MANAGER`
entry and reciprocal persistent reactor. Existing anchors, including an orphan
registered manager under either source spelling, prevent duplicate creation.

The original CLASS checks are retained. An existing canonical definition is
validated and an explicitly present instance count is updated; absent definitions
and counts are not synthesized. Allocation checks the complete document handle
space, including owner-held attributes not in the top-level registry, before
assigning the manager's single handle.

`ReplaceSections` validates the manager's original source profile, exact source
registration, root anchor and persistent reactor sequence, and then replaces its
packet and member list. Previously returned containers retain their old content;
the referenced SECTION objects remain live. The update flag, count and ordered
330 pointers are regenerated without changing the manager identity or allocating
new handles.

`EraseSectionManager` validates the source-bound manager before using the existing
whole-graph erasure preflight. Incoming references block erasure. Accepted erasure
removes owning aliases, extension descendants, registration and APPID bookkeeping,
but does not erase the referenced SECTION objects. The manager keeps its handle
as an erased identifier and cannot be reused. Generic ownership erasure and
cloning cannot bypass this dedicated lifecycle. Existing CLASS metadata is retained
while its present instance count is updated.

```js
import { DxfDocument, Section } from './javascript/index.js';

const document = new DxfDocument(18); // Pinned AutoCad2018 enum value.
const section = new Section();
document.Entities.Add(section);
const manager = document.Objects.CreateSectionManager([section, section], false);
const earlierMembers = manager.Sections;
console.assert(document.Entities.Remove(section) === false);
manager.ReplaceSections([], true);
document.Objects.EraseSectionManager(manager);
console.assert(earlierMembers.Count === 2);
console.assert(manager.IsErased);
console.assert(document.Entities.Remove(section));
```

## Callback and language contracts

Creation and replacement finish enumeration, including disposal, before their
commit point. Exceptions from acquiring the enumerator, moving, reading Current
or disposing do not install a partially constructed packet. Caller side effects
are not rolled back; affected source state is revalidated afterwards. Disposal
exceptions preserve their source precedence over an earlier enumeration failure.

The source deliberately treats recursive creation and replacement differently:
a caught recursive creation attempt still invalidates the outer creation, whereas
a caught replacement re-entry error does not by itself invalidate the outer edit.
Both guards reset after failure so a subsequent valid operation can run.

`runtime/ManagedEnumerable.js` adapts synchronous `IEnumerable<T>` using explicit
`GetEnumerator` / `MoveNext` / `Current` / `Dispose` methods. Native JavaScript
iterables are also supported, with `return()` as the explicit cleanup hook before
commit, including normal exhaustion. This is a documented adapter contract, not
a claim that every CLR interface or asynchronous iterator is emulated.

The exported manager constructor and Resolve hook adapt internal source-reader
entry points. Normal authoring uses CreateSectionManager. Retained packets keep
their original spelling, anchor casing/strength and reactor snapshot. Synthetic
internal-loader fixtures exercise those rules independently; they are not
counted as loading a DXF through the still-missing typed reader.

## Verification design

The input-only corpus contains **174 scenarios / 5,406 operations**. Separate
native C# and JavaScript controllers call actual implementation APIs and observe
results, identities, packets, registry state, errors and callback counters. There
is no expected-output table in production code or normalization of differences.
Malformed or terminated native observations remain failures.

Cases cover profiles, repeated/empty/maximum membership, CLASS conflicts and count
presence, invalid identities, handle exhaustion and owner-held handles, every
iterator phase, caught and uncaught re-entry, callback mutation, retained record
invariants, aliases, descendants, incoming pointer kinds and 24 deterministic
randomized edit sequences. Twenty-five supplemental tests additionally exercise
package identities, immutable historical containers and cleanup semantics.

This category is mandatory in aggregate qualification. All 174 browser scenarios
are appended after existing retained-polyline and annotation inputs; both browser
modes require at least **141,186 comparisons**. The installed-package check imports
the barrel and standalone manager path. The existing Linux/Windows Debug/Release
workflow runs all four ownership/annotation/retained/manager corpora, the focused
regressions and the full mirrored original JavaScript suite.

No original SECTION_MANAGER case is shortened to omit typed Save/Load. The new
25 tests are supplemental and do not increase original-case identity coverage.

## Recovery and remaining scope

The `fae312d6` source archive was restored and its complete tree verified. No
surviving uncommitted project changes were found. The newer documentation-only
`2374951` checkpoint arrived before publication and was preserved, including its
retained-polyline evidence. The manager implementation was committed on top of
that newer head; no force push or discarded remote change was used.

The current ledger is **368/510 library mirrors (142 missing), 56/193 original
conformance-file mirrors (137 missing), and 2,905/35,309 original JavaScript cases
(32,404 missing)**. File presence is not exhaustive API or behavioral equivalence.

Typed DXF reading/writing and Load/Save/SaveAtomic integration, complete profile
conversion, ACAD_TABLE and other private schemas, remaining APIs and original
cases/examples, cross-runtime numerics and broad platform/filesystem/performance
acceptance remain unfinished. Source-bound SECTION_MANAGER lifecycle support
does not establish native application open/AUDIT/save/reopen fidelity. The raw
API remains separate and is not substituted for missing typed transport.

PR #98 remains draft. Original C# sources/tests/fixtures are unchanged; no merge,
npm publication or failing-test waiver was performed.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/SECTION_MANAGER.md). Current published scope is maintained in the [README](../README.md).
