# Observable dictionary contracts and qualification

Runtime checkpoint: `6675a4af8c13ddcd67a5995c2aa406a50742df24`.
Verification checkpoint: `f6ac4dd636d1979b4b0c96fe200355cffbffaa16`.
Behavioral baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.
The original C# library, original conformance tests and shared DXF fixtures are unchanged.

## Public surface and language adapters

`netDxf/Collections/ObservableDictionary.js` and
`netDxf/Collections/ObservableDictionaryEventArgs.js` mirror the original source
paths and export through the package barrel and standalone subpaths. The storage
helper is native JavaScript; it does not dispatch to .NET or a native dictionary.

The four constructor forms are `()`, `(capacity)`, `(comparer)` and
`(capacity, comparer)`. For erased generic value semantics, use
`(capacity, comparer, keyType, valueType)`. A custom comparer supplies
`GetHashCode(key)` and `Equals(left, right)`. Value-type descriptors determine
missing-value defaults and pair-removal boxing behavior; a struct constructor
such as `Vector2` also supplies the existing value-copy protocol.

The class exposes `Count`, `IsReadOnly`, `Keys`, `Values`, `get_Item`, `set_Item`,
`Add`, `Remove`, `Clear`, `ContainsKey`, `ContainsValue`, `TryGetValue`,
`GetEnumerator` and JavaScript iteration. `TryGetValue(key, output)` returns the
success Boolean and writes `output.value`. `AddPair`, `RemovePair`, `ContainsPair`
and `CopyTo` adapt the explicit generic `ICollection<KeyValuePair<TKey,TValue>>`
implementation. Pairs have read-only `Key` and `Value` accessors.

For an internally installed package:

```js
import { ObservableDictionary, Vector2, BoxedString } from '@netdxf/javascript';

const points = new ObservableDictionary(0, null, 'string', Vector2);
points.BeforeAddItem.Add((sender, args) => {
  if (args.Item.Key === 'reserved') args.Cancel = true;
});
points.Add('origin', new Vector2(0, 0));
points.set_Item('origin', new Vector2(2, 3)); // Replacement, not upsert.
const output = {};
points.TryGetValue('missing', output); // false; output.value is a new zero Vector2.

const labels = new ObservableDictionary(0, null, 'string', 'string');
const shared = new BoxedString('label');
labels.Add('a', shared);
labels.RemovePair({ Key: 'a', Value: new BoxedString('label') }); // false
labels.RemovePair({ Key: 'a', Value: shared }); // true
```

A JavaScript primitive cannot represent every CLR string or boxed-scalar object
identity. Use a shared `BoxedString` or `BoxedScalar` adapter when identity matters.
Nonempty primitive-string identity is deliberately not inferred from equal text.
Empty strings follow the shared-empty representation. Value equality remains
separate and accepts equivalent primitive and explicit string representations.

## Source behavior preserved

Replacement first reads an existing value, then raises `BeforeRemoveItem` and
`BeforeAddItem`. Cancellation short-circuits in that order. After replacement it
raises `AddItem` before `RemoveItem`. A missing replacement key throws before
callbacks. `Add` raises its before-event before duplicate/null-key validation;
cancellation throws `ArgumentException` with parameter `value`.

Events use multicast snapshots. A later subscriber may undo cancellation, and
callback exceptions preserve already-completed changes. Re-entrant removal can
produce both inner and outer removal notifications, matching the source rather
than suppressing the outer notification. Event pairs copy value-type keys and
values but retain reference-type identity.

`Clear` snapshots the keys and calls `Remove` for each. Cancelled removals, keys
whose stored hash no longer matches, and keys inserted by callbacks can survive.
`ContainsPair` uses value equality. `RemovePair` first looks up the key, throwing
when absent, and then uses reference identity; equal value-type boxes do not
satisfy that identity test.

The backing storage retains freed-slot reuse, collision-chain lookup order,
custom equality and resizing. An uninitialized dictionary does not call a
comparer for missing lookups, but still rejects null keys. A failed first insert
can leave the dictionary allocated, changing subsequent comparer observations.

`Keys` and `Values` are cached live read-only views. Generic enumeration copies
value types and retains the current-item snapshot. Successful insertion
invalidates existing enumerators; overwrite, removal and clear do not. Empty
view enumeration preserves the pinned BCL's special empty-enumerator behavior,
including throwing `Current` and immunity to subsequent insertions. The main
dictionary's generic enumerator remains a real versioned enumerator when empty.

## Independent verification

`tools/observable-dictionary-corpus.mjs` defines inputs only: **427 scenarios and
15,799 requested operations**. `GeometryOracle/ObservableDictionaries.cs`
observes the unchanged native generic class. The JavaScript observation adapter
calls the production port. Both expose collection state, results, event order,
comparer calls, exception type/parameter and exact binary64 observations.

Two invalid constructors reject their scenarios before four requested operations
can execute. Reports therefore distinguish **15,795 executed operations and two
constructor rejections** from requested operations. No unexecuted operation is
fabricated as a successful comparison. Malformed responses or terminated native
processes remain failures, with a new process for subsequent observations.

Coverage includes all constructor patterns; capacity/hash timing; custom and
throwing comparers; cancellations and subscriber order; callback exceptions;
re-entrant mutation; live views and copying; iterator state; mutable and
non-reflexive keys; copied structs; reference-sensitive pair removal; boxed
values; signed zero and NaNs; and 144 deterministic randomized scenarios.

The category is mandatory in `run-qualification.mjs` and `verify.mjs`. All 427
browser scenarios are appended after previous inputs without removing or
reordering them. Both browser modes require at least **137,604 comparisons**.
The offline package smoke suite imports the barrel and standalone module and
checks events, identity and value copies. The dedicated read-only GitHub Actions
workflow covers Ubuntu 22.04 and Windows 2022, each in Debug and Release.

From `javascript/`, with the pinned C# checkout and toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:observable-dictionaries
node --test tests/unit/observable-dictionaries.js
npm run test:unit
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
npm run verify:complete
```

## Source-bound local results, September 21, 2026

These results describe the executable tree uploaded as `f6ac4dd`, not a claim
that later hosted jobs passed. SDK 8.0.425 / .NET 8.0.31, Node 22.16.0,
Linux x64, Chromium 144.0.7559.96:

| Check | Observed result |
| --- | --- |
| Dictionary Debug and Release | Each: 427 scenarios; 15,795 executed operations; two constructor rejections; zero mismatches or unavailable native observations |
| Original unchanged C# conformance | Each configuration: 35,309 passed, zero failed |
| Ported original JavaScript conformance | 2,820 passed, zero failed |
| Supplemental JavaScript tests | 750 passed, zero failed/skipped/TODO; includes 24 new dictionary tests |
| Offline installed runtime package | Passed; 431 files; private, not published |
| Release inline native-ESM Chromium | Executed all 137,604 comparisons; no page errors or unavailable source observations; zero dictionary mismatches; 75 failures in other scenarios |
| Full completion gate | Failed; retains missing source/tests and failed or unavailable verification categories |

Runtime fingerprint:
`322d304d54c8c9bc7748b85c7083d2801049beae04af5df29fc85fb4482e6328`.
Verification fingerprint:
`21136aecf13b179ec91226724aa06f16455414280f503a2f0ff403301ce1c20d`.
Source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

The separate HTTP-served browser mode and all other standalone differential
categories were not rerun locally for this checkpoint. Their absence is reported,
not treated as success. Hosted runs and artifacts remain separate evidence.

## Hosted dictionary matrix

GitHub Actions run `35647382493`, attached to `f6ac4dd`, completed successfully
on Ubuntu 22.04 and Windows 2022 in both Debug and Release. All four jobs passed
the exact differential and all 24 focused unit tests. Each downloaded result
archive was checked against its GitHub SHA-256 digest; all reports record 427
observed scenarios, 15,795 executed operations, two constructor rejections and
zero failures.

[The retained hosted receipt](observable-dictionaries-hosted-f6ac4dd.json)
contains the four actual result documents, artifact IDs, archive digests,
result-file hashes and native environment observations. The runtime fingerprint
matches across all profiles. The existing verification fingerprint sorts host
paths before separator normalization; its Windows value is
`280456b6732357470c70a1d8e7c4380b35bd1807d334221afbbb1dd1cddd0a87`.
Both host-specific verification hashes were independently reproduced from the
same checked-out file bytes. The original reports are retained without rewriting
those hashes. This qualifies this collection corpus on those four profiles,
not the complete JavaScript port.

## Remaining scope

This adds two source mirrors, not complete typed document ownership or IO.
The current file ledger is **311/510 library mirrors (199 missing)** and
**2,820/35,309 original JavaScript cases (32,489 missing)**. Supplemental tests do
not count as original test identities. File presence is not member/API or
behavioral qualification.

The collection corpus covers string, Int32, Double, Vector2, reference/object and
explicit boxed-value cases. It is not exhaustive over every CLR generic type,
localized exception message, memory-pressure condition or custom comparer.
Generic enumerators and explicit generic collection adapters are provided;
reflection, reified CLR type checks and nongeneric interface casts are not a
complete CLR emulation. Arbitrary unsynchronized mutation inside comparer
callbacks is not qualified as an extension of supported event re-entrancy.

The complete port still needs typed `DxfDocument`, registered entity/table
ownership, typed reader/writer and remaining public APIs, original tests and
examples, exact cross-platform numerics, filesystem guarantees and performance
qualification. The aggregate, browser and full-port gates remain strict. PR #98
stays a draft; no merge, force push, npm publication or expected-failure waiver.
