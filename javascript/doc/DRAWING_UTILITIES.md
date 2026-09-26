# Drawing-time and enum utility checkpoint

Executable checkpoint: `0b23eeda0b63307c675b5629cb5ddbd4c161bce3`.
Tree: `1e8faba94f3fdd07d222d58be2c92279432049e9`.
Baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK 8.0.425,
.NET 8.0.31, Node 22.16.0. This is not a complete JavaScript port.

## Recovery and publication

The available files contained source archives and earlier validation records, but
no surviving uncommitted checkout or unpublished implementation patch. The
current `6852f80` source archive was downloaded from GitHub artifact `10679807839`.
Its SHA-256 is `65258a9b0a0178c704ee4552f98c6feab93cca2cabb8a94b7af56e66b004c8bf`.
Restoring every archived tracked path, including ignored tracked fixtures,
reproduced tree `5c4067685b4149a90f9504ed8ad987eca370f87d` exactly.

The prior dimension recovery, concrete dimension integration, document reference
accounting, and Windows generator corrections were already published. They were
preserved, not recreated or presented as newly recovered unpublished bytes.
`88d3168` adds DrawingTime; `0b23eed` adds StringEnum and required integration.
Both uploaded trees matched their local staged trees before the fast-forward.

## DrawingTime

`netDxf/Units/DrawingTime.js` provides `ToJulianCalendar`, `FromJulianCalendar`,
and `EditingTime` at the original relative path and through the package entries.
DateTime and TimeSpan values use the existing immutable `HeaderDateTime` and
`HeaderTimeSpan` adapters, with BigInt 100-nanosecond ticks.

The implementation retains the source's arithmetic, not an alternate Julian-day
formula: it uses wall-clock components without converting DateTimeKind, truncates
sub-millisecond ticks, and uses the source's integer-day epoch. The reverse
conversion accepts the original inclusive range 1721426 through 5373484 and
returns Kind=Unspecified. The final-day fractional input remains out of range.
Unchecked Int32 conversions, NaN component-validation order, signed durations,
and overflow checks are preserved. EditingTime accumulates the constructor's
integral millisecond total before converting it to ticks.

```js
import { DrawingTime, HeaderDateTime } from '@netdxf/javascript';
const julian = DrawingTime.ToJulianCalendar(HeaderDateTime.MinValue); // 1721426
const date = DrawingTime.FromJulianCalendar(2451545.5); // 2000-01-01 12:00
const duration = DrawingTime.EditingTime(-1.25); // -1080000000000n ticks
```

The input-only corpus covers endpoints, adjacent binary64 values, sub-millisecond
values, calendar boundaries, all three kinds, signed durations, overflow, NaN
payloads, clock fractions and deterministic randomized values. Its **1,497
scenarios / 3,879 operations** match the actual pinned C# methods in each locally
executed and hosted configuration. Ten focused regression tests are supplemental,
not additional original conformance identities.

## StringEnum and StringValueAttribute

`netDxf/StringEnum.js` implements the helper over all **77 pinned generated enum
objects**. `StringEnum.For(EnumType)` binds the erased generic type and returns a
cached class with the source's parameterless constructor and static signatures.
Alternatively, construct `new StringEnum(EnumType)` and use the explicit-type
static forms. `EnumType` returns that enum object, not a CLR reflection Type.

```js
import { StringEnum, StringComparison, DxfVersion } from '@netdxf/javascript';
const Versions = StringEnum.For(DxfVersion);
const helper = new Versions();
Versions.Parse('ac1032', StringComparison.OrdinalIgnoreCase); // 18
Versions.GetStringValue(DxfVersion.AutoCad2018); // 'AC1032'
helper.GetValues().get_Item(DxfVersion.AutoCad2018); // 'AC1032'
```

`GetStringValues` and `GetValues` return independent mutable list/dictionary
snapshots in source declaration order. `GetStringValue`, `Parse`, and
`IsStringDefined` retain null/default behavior, undefined values, unannotated
enums and comparison-option validation. An enum with no attributed strings
performs no comparisons in IsStringDefined, so an invalid comparison option is
not evaluated there. StringValueAttribute retains a read-only Value, including
null or empty text.

Ordinal comparison uses the existing pinned ordinal casing adapter. Linguistic
comparison uses host ICU via Intl.Collator. For the registry's ASCII attributed
labels, ignore-case comparison also preserves the width distinction specified
by the .NET ICU tailoring. This follows `CloneCollatorWithOptions` and
`FillIgnoreWidthRules` in the .NET Foundation's
[MIT-licensed runtime source](https://github.com/dotnet/runtime/blob/v8.0.0/src/native/libs/System.Globalization.Native/pal_collation.c).
Case-sensitive Japanese culture behavior remains distinct from that ignore-case
tailoring; it is not replaced by ordinal matching. No expected outputs are used
as runtime lookup tables or to normalize comparator results.

The expanded input-only corpus has **167 scenarios / 28,788 operations**, covering
all 77 enum types and invariant, English, Turkish, Polish and Japanese culture
profiles. It includes nulls, invalid modes, embedded NUL, ignorable characters,
case, width, undefined underlying values and fresh snapshots. Twelve focused
regressions supplement the independent C# reflection observations.

**Boundaries:** arbitrary user-defined CLR enums, aliases, runtime reflection,
attribute discovery on external assemblies and exhaustive Unicode/ICU-version
compatibility are not provided by this registry. A passing finite corpus is not
an assertion of general CLR globalization equivalence.

## Mandatory integration and completed evidence

Both new stages are required by `run-qualification.mjs` and `verify.mjs`.
The 1,664 browser scenarios are appended after every previous input, with the
comparison minimum raised to **140,531**. Installed-package checks exercise the
barrel, standalone paths, tick conversions, generic binding and snapshot isolation.
The original C# files, original tests, shared fixtures and prior comparisons are
unchanged. There are no expected-failure allowlists or relaxed tolerances.

Source-bound local results for the executable checkpoint, September 22, 2026:

| Check | Observed result |
| --- | --- |
| DrawingTime, Debug and Release | Each: 1,497 scenarios / 3,879 operations, zero mismatches |
| StringEnum, Debug and Release | Each: 167 scenarios / 28,788 operations, zero mismatches |
| Full unchanged C# suite | Each configuration: 35,309 passed, zero failures |
| Original JavaScript suite | 2,820 passed, zero failures or unexpected identities |
| Supplemental JavaScript suite | 826 passed, zero failures, skips or TODOs |
| Offline-installed package | Passed, 448 files |
| Native and concrete-dimension regeneration | Both source-derived manifests reproduced exactly |
| Release foundations | 5,185 scenarios / 49,421 operations and 256 emitted-byte comparisons, zero mismatches |
| Release randomized geometry | 2,000 exact comparisons, zero mismatches |
| Release inline Chromium 144.0.7559.96 | All 140,531 comparisons executed, no utility mismatches or page errors; 83 existing failing scenarios remain |
| Complete verification, both configurations | Failed; all unavailable or failing categories retained |

The initial Release C# attempt was interrupted by the local execution timeout.
Its stale writer lease was removed only after checking that its owner/compiler
had exited. The complete fresh run subsequently passed; the interrupted attempt
is not counted as a completed test result.

Hosted run [35702158923](https://github.com/wieslawsoltes/netDxf/actions/runs/35702158923)
at `0b23eed` passed **all four Ubuntu 22.04 / Windows 2022, Debug / Release jobs**.
Each ran both complete utility corpora and all 22 focused tests. All four ZIPs
were downloaded, checked against GitHub SHA-256 digests and inspected. The
[retained receipt](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/drawing-utilities-hosted-0b23eed.json) includes all eight actual
result documents and their hashes. These jobs qualify only the utility corpus,
not the broad JavaScript workflow or full source translation.

Runtime fingerprint: `c5c2c25508dd03fca8541ef5fb812a49aa77e517ea9be47bf827a01a15da52c4`.
POSIX verifier: `aa65c4d823e49cd217464d8ef8cb0b8e347c461a5a4ade0c0ad8b98165e6127e`.
Windows verifier: `b787fb76619ae80e577408071c68a454280e519b746e92745c442ce46dbe700d`.
The existing verifier sorts host paths before separator normalization; both
host-specific verifier hashes were independently reproduced from the same files.
Documentation-only changes do not alter those executable fingerprints.

## Remaining parity work

Current source presence: **324/510 library mirrors**, with **186 missing**.
Original conformance presence: **53/193 files**, with **140 missing**.
Original JavaScript cases: **2,820/35,309**, with **32,489 missing**.
Presence is not exhaustive member/signature or behavioral qualification.

The Release concrete-dimension comparison retains 26 nonfinite transform
operation differences. Debug retains six unavailable native scenarios caused by
source assertions. The browser's 83 failures comprise 64 entity, 12 concrete
dimension, four block, two leader and one coordinate scenario. None belongs to
the added utility corpora. The separate HTTP-origin and Debug browser runs and
other unexecuted standalone categories are not counted as passing.

Typed DxfDocument, registered entity/table/database ownership, complete typed
reader/writer, remaining public APIs and original tests/examples, cross-platform
numeric and filesystem guarantees, and performance acceptance remain unfinished.
PR #98 stays a draft and npm publication remains blocked.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/DRAWING_UTILITIES.md). Current published scope is maintained in the [README](../README.md).
