# Codec and stream contracts

Low-level code/value codecs, raw documents and typed documents are separate APIs.
The integrated typed Load/Save pipeline uses these components but does not make
their framing, buffering or failure guarantees interchangeable.

## Readers

Text readers classify group codes before parsing values. Integral ranges,
finite doubles, booleans, handles and binary chunks retain their own diagnostic
kinds and positions. Integer group code `-0` becomes zero; floating-point negative
zero remains a binary64 value. Code, the previous typed Value and consumed input
can remain observable after a parse or caller callback fails.

Binary readers consume actual stream methods, including fragmented reads.
Structural errors, value errors, flush/Position behavior and nonseekable streams
have different failure paths. Neither low-level reader closes caller-owned
streams. Header probes preserve the source-prescribed stream position and public
versus internal failure contracts.

Typed unterminated-section handling reports `EndOfStreamException` where the
source requires it. The public raw reader retains its separate `FormatException`
framing contract. Other errors must not be relabeled wholesale, and deferred
error ordering must never turn invalid framing into a successful document.

The raw document loader owns a bounded byte snapshot for retention/indexing; this
is distinct from the low-level stream reader's incremental behavior. Buffer
conveniences do not imply arbitrary browser file-handle or System.IO support.

## Writers

Text codecs preserve the caller's WriteLine overload/callback behavior and
invariant wire formatting; display formatting is separate. Typed text saving
captures `Culture.NewLine` when creating its writer. Raw/direct codec output
retains its own newline policy. Callback changes must not alter a captured typed
newline halfway through a document.

Binary CurrentPosition flushes before accessing Position. Scalar writes use
call-local buffers to preserve outer values during reentrant writes. String
terminators, byte calls, empty writes and chunk framing retain their separate
callback behavior. Binary chunk validation happens before changing the current
tag or emitting its group code.

UTF-8 character-array output preserves the pinned BinaryWriter's chunking and
invalid-scalar failure state. Completed chunks remain written if a later scalar
fails; that failure does not roll back output or invent a terminator. The
adaptation's .NET Foundation notices remain in the package.

Do not infer transactionality from preflight alone. Low-level writes may leave a
completed prefix, typed saving may mutate selected source metadata, raw saving
stages its serialized output before copying, and `SaveAtomic` uses a separate
filesystem staging/publication boundary. See [FILESYSTEM.md](FILESYSTEM.md).

## Verification

`test:codec-readers` and `test:codec-writers` execute independent native and
JavaScript observation hosts. They compare bytes/text, consumed offsets, current
code/value, exceptions, callbacks, flushes, lifetime and reentrant effects.
Constructor failures must not create fictitious command results. Test corpora
and original modules, not old counts in documentation, define required inputs.

Run the relevant original scalar/framing modules, the complete mirrored subset,
the supplemental suite and installed-package tests. Native and browser lanes need
separate fresh evidence. [VERIFICATION.md](VERIFICATION.md) gives setup and output
locations. Local roundtrips do not establish native wire or failure-order parity.
