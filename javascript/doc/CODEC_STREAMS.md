# Codec stream state, diagnostics and writer framing

Reader checkpoint: `160113047715849252757502ba3d2cc8688b3d13`.
Expanded executable checkpoint: `fc0735f1125bfbb44d00f1dff2aaf6b0797fb72e`.
Executable tree: `5dd1f61ed82fc3a2775d6bc328af59ae7f815425`.
Unchanged C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This increment improves the four existing code/value readers and writers. It
**does not implement the still-missing typed DxfDocument Load/Save pipeline**.
Exact low-level codec behavior and full typed document transport are separate
qualification categories. The package remains private and PR #98 remains draft.

## Reader corrections

Text parsing now retains the source's exact diagnostic kinds and positions:
16-bit/32-bit/64-bit integer failures, Boolean and finite-real failures, malformed
handles, odd-length binary chunks, and the byte index of an invalid hex pair.
An unknown text group retains the quoted group code and logical input line.
The current code, prior typed Value and consumed-line state remain observable
when parsing or a caller ReadLine callback fails.

A parsed integer group code of `-0` becomes integer zero. Floating-point values
still preserve negative zero and their exact bits. Comment skipping, Code5IsString,
primitive casts and continuation after a failed Next retain source behavior.
Both reader ToString implementations now use the established culture-aware
formatter, including empty null values, native Boolean spelling and System.Byte[].

Binary structural errors differ from value errors: unknown/comment group errors
read the underlying stream Position even when CanSeek is false. The resulting
stream exception, previous Value and bytes already consumed are preserved.
The previously published actual-stream cursor remains in place; fragmented
reads invoke the caller's Read/ReadByte methods without a whole-input copy or
closing the caller's stream. Header probes preserve the caller's original position
and follow the source's public versus internal failure contracts.

Exception.Message is exposed as a native-style accessor; ArgumentException.Message
includes the parameter name while the existing JavaScript message string remains
backward compatible. This is not exhaustive CLR localized exception formatting.

## Writer corrections

Text writers preserve constructor/null failure timing and the native WriteLine
overloads: group codes and Boolean values invoke the integer callback while other
numeric values use invariant text. Failed output retains the native current
Code, previous Value and logical position. Re-entrant output can update that state,
and an unknown-code diagnostic observes the resulting state rather than a stale
outer snapshot. ToString remains culture-sensitive while serialized numeric text
remains invariant.

Binary CurrentPosition flushes before reading Position, matching the source
BinaryWriter.BaseStream behavior. Flush failures and nonseekable Position errors
retain their native ordering. WriteByte invokes the byte callback where supplied;
encoded string terminators use the array Write callback, including the observable
empty-array write for an empty string. The constructor emits a fresh signature
buffer instead of exposing the shared signature to a mutable caller callback.

Numeric writes now use call-local buffers. A caller that re-enters the writer
before copying the outer buffer can no longer corrupt that outer numeric value.
Binary chunk preflight still rejects null/wrong-type/oversized chunks before
changing the current tag or emitting its group code; no destructive fallback or
silently truncated length is introduced.

UTF-8 character-array framing preserves the pinned BinaryWriter chunk boundaries
and encoding-failure state. Large writes emit up to 65,536 encoded bytes per chunk
without splitting Unicode scalar values. A malformed UTF-16 scalar immediately
after a full chunk is validated before the capacity test, matching native
Encoder.Convert behavior. A later malformed scalar preserves completed earlier
chunks; it does not roll back bytes already written or emit a terminator.

The helper adapts the .NET Foundation's MIT-licensed BinaryWriter source. The
existing full notice and the added THIRD_PARTY_NOTICES entry remain in the package.
This is a UTF-8 framing adaptation, not complete arbitrary-encoder buffering or
System.IO emulation.

## Independent verification contract

Separate C# and JavaScript observation hosts call the unchanged native codecs and
the actual production port. The hosts record bytes/text, callback order, consumed
offsets, current code/typed value, exception type and parameter, flushes, stream
lifetime and re-entrant effects. They do not implement expected serialization.

The reader/probe corpus contains **913 scenarios**, **27,962 requested commands**,
**27,407 executed commands** and **35 constructor rejections**. The writer corpus
contains **509 scenarios**, **5,527 requested commands**, **5,495 executed commands**
and **five constructor rejections**. A rejected constructor does not fabricate
successful command observations. Strict transport validation rejects incomplete
or malformed envelopes, missing fields and incorrect command counts.

Together these are **1,422 scenarios / 32,902 executed commands**, plus 40
constructor rejections. Inputs cover all group-code ranges, numeric limits,
positive/negative zero, malformed data, culture formatting, modern/legacy binary
codes, seekable/nonseekable streams, fragmented reads, injected failures,
re-entrant writes, large Unicode buffers and deterministic randomized values.

Source-owned parse/structural diagnostics and injected IO exception messages are
compared directly. CLR-owned cast/null/encoding/default argument wording is not
claimed as a universal localized-message contract: those checks compare type,
parameter, state, output and callback observations. Neither native results nor
numeric outputs are normalized to waive mismatches.

## Original cases and mandatory integration

**564 complete original cases** are newly ported: 110 BinaryChunkTests cases,
89 BinaryChunkWriterTests cases and 365 DoublePrecisionWriterTests cases. They
retain the original identities and assertions, including all chunk lengths,
all truncated prefixes, recovery after rejected writes, exact byte/record alignment,
all double-valued group codes and the original 4,096-value randomized bit corpus
in four cultures. The three new test paths do not imply all cases in every file
are complete: the six typed-XData writer cases and twelve typed-document precision
cases remain unported rather than having Load/Save assertions removed.

The **40 new focused tests** are supplemental: 19 reader tests, 17 writer tests
and four observation-transport tests. They do not inflate original-case coverage.
Both comparison stages are mandatory in aggregate qualification and verification.
All 1,422 browser inputs are appended after earlier inputs without deletion or
reordering, raising the required browser count to **144,318**. The offline smoke
test imports installed standalone modules, not the source checkout or a .NET oracle.

The new read-only workflow uses the pinned toolchains and source reference on
Ubuntu 22.04 and Windows 2022 in Debug and Release. It retains the optional Windows
atomic-replacement host build before running the entire mirrored original suite.

## Remaining parity work

The ledger is **394/510 library source mirrors (116 missing)**,
**65/193 original conformance-file mirrors (128 missing)** and
**3,587/35,309 original cases (31,722 missing)**. This turn corrects existing codec
implementations; it does not add library-path stubs or claim file presence is an
exhaustive public API/signature/behavioral audit.

Release retains 26 concrete-dimension, six LEADER, eight block, three coordinate
and 128 entity operation differences. Debug retains one cubic Bezier NaN-sign
comparison and 22 unavailable native scenarios from original assertions: six
dimensions, twelve tolerances and four layout/viewports. Both casing stages reject
the local Debian globalization profile before executing their pairwise comparisons;
reported precomputed pair counts are not counts of executed successful comparisons.

The current Debug browser count is 31, compared with 27 in the last documented
retained-dependency checkpoint: 22 unavailable native observations, eight
concrete-dimension digest differences and one cubic Bezier difference. No new
codec scenario fails. The differing numerical counts are retained as failures,
not presented as a numerical correction or waived because focused tests pass.

Full typed DXF reading/writing, DxfDocument Load/Save/SaveAtomic integration,
version conversion, remaining private/TABLE/evaluator APIs, missing original tests
and examples, arbitrary stream/encoding/async/concurrency guarantees, exhaustive
platform qualification and release performance acceptance remain unfinished.
Opaque/version analysis published before this turn is preserved but its missing
unpublished dedicated comparison harness is not relabeled as recovered evidence.
No AutoCAD open/AUDIT/save/reopen qualification is claimed.

No original C# source or shared fixture changes, comparison removal, tolerance,
expected-failure waiver, merge, force push or npm publication occurred.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/CODEC_STREAMS.md). Current published scope is maintained in the [README](../README.md).
