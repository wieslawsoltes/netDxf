# Filesystem and atomic saves

## API and host boundary

Both raw and typed documents expose synchronous `SaveAtomic(file, binary?,
cancellationToken?)`. The original paths include `IO/DxfAtomicFile.js`,
`IO/DxfRawDocument.AtomicSave.js` and `DxfDocument.AtomicSave.js`. The cancellation
adapter accepts a token with `ThrowIfCancellationRequested()` or an AbortSignal.

The portable package entry does not install filesystem access. Import
`@netdxf/javascript/node` to install the synchronous Node host and export
`FileStream`. Caller-owned `Load(stream)` and `Save(stream)` streams remain open.
Ordinary saving is not a filesystem transaction and must not be described as
having SaveAtomic's destination-publication guarantees.

## Staging and publication

The helper validates arguments, cancellation, the path and existing destination
before staging. It exclusively creates a random `.netdxf-*.tmp` sibling,
serializes, checks cancellation, flushes, closes the staging handle, rechecks
cancellation/existence and then publishes. Serialization, cancellation, flush,
and detected existence-change failures do not publish an incomplete prefix.
Asynchronous serializers are rejected by this synchronous contract.

Existing destinations use same-directory rename on non-Windows hosts and
`ReplaceFileW` through the optional addon on Windows. New destinations use an
exclusive hard link followed by temporary-name cleanup. A destination created
concurrently must not be overwritten. There is no in-place copy, delete-first
or copy/delete fallback. Unsupported hard links cause failure rather than a
weaker publication strategy.

Cleanup is attempted after success and failure. Cleanup errors must not conceal
the original serialization/publication error. A successful publication can still
leave an inert temporary sibling after cleanup failure; it must not be reported
as an unpublished destination. Atomic destination publication does not promise
rollback of every in-memory source mutation made by typed serialization.

## Limits

Destination symlinks, nonregular files and read-only files are rejected. Parent
symlink/namespace races, concurrent changes to an existing destination, metadata
and ACL preservation, arbitrary sharing modes and all System.IO overloads are not
fully modeled. The API does not promise directory-fsync or power-loss durability.
It is not a filesystem isolation transaction.

The host stream supports Open/CreateNew/Create with Read/Write/ReadWrite, byte and
bounded-buffer I/O, partial-write draining, Position/Length, SetLength, Flush and
Dispose/Close. Positions must be exactly representable safe JavaScript integers.
Additional asynchronous, FileShare/FileOptions and browser file-handle contracts
need separate adapters and qualification.

## Verification

Original atomic-save tests cover raw and typed paths; filesystem differential
corpora compare the actual pinned C# and JavaScript results. Supplemental tests
cover publication races, short/zero writes, flush errors, cleanup, reentrancy and
wrong serializer behavior. No one category substitutes for another.

```sh
# From javascript/, with the exact pinned C# source/toolchain:
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
npm run test:filesystem
DXF_TEST_FILTER=atomic/ npm test
npm run test:package
```

The filtered command is diagnostic, not complete original-suite evidence.
Windows needs a real host run and a built addon; see
[WINDOWS_HOST.md](WINDOWS_HOST.md). Never infer Windows success from Linux tests
or old CI results. Report storage and freshness rules are in
[VERIFICATION.md](VERIFICATION.md).
