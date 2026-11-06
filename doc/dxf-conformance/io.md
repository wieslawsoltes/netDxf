# Loading, saving and raw preservation

## Typed loading and stream ownership

Use `DxfDocument.Load` for the semantic object model. Check its result and the
result of ordinary `Save`: some parser/writer failures throw in Debug and return
`null` or `false` in Release. Argument, setup, file-opening and unsupported-version
errors can still throw in either configuration.

File-path overloads dispose the streams they open, including failure paths.
Stream overloads leave caller-owned streams open. Ordinary file saving can
truncate the destination before serialization completes; it is not transactional.
Supply support folders for external resources and handle missing resources
explicitly. Loading a drawing does not embed every referenced image, font or
underlay. The [support-folder tests](../../tests/netDxf.Conformance/SupportFolderLookupTests.cs)
exercise lookup order and path behavior.

## Saving files

Use `SaveAtomic` when replacing a destination whose previous bytes must survive
a failed serialization:

```csharp
document.SaveAtomic("drawing.dxf", isBinary: false, cancellationToken: token);
rawDocument.SaveAtomic("preserved.dxf", cancellationToken: token);
```

These overloads throw on failure in both build configurations. They create a
unique temporary file beside the destination, serialize and flush it, then use
`File.Replace` for an existing file or `File.Move` for a new one. Unsupported
replacement fails instead of falling back to delete-and-copy. Staging cleanup
is best effort and does not mask the primary exception.

This protects destination bytes, not every in-memory writer mutation. It does
not provide concurrent-writer isolation, hostile-filesystem protection or a
universal power-loss guarantee. Read-only files, directories and destination
symbolic links/reparse points are rejected. Cancellation after the last checkpoint
cannot undo a committed rename. See the [implementation](../../netDxf/DxfDocument.AtomicSave.cs)
and [atomic-save regressions](../../tests/netDxf.Conformance/AtomicSaveTests.cs).

## Preserve unfamiliar data

`DxfRawDocument` retains ordered tags and the original byte stream. An unedited
same-transport save writes the original bytes, including line endings and numeric
spelling. Editing or changing transport normalizes lexical formatting while
preserving the admitted ordered primitive values.

```csharp
using var input = File.OpenRead("drawing.dxf");
var raw = DxfRawDocument.Load(input, new DxfRawOptions(
    maximumBytes: 128 * 1024 * 1024,
    maximumTags: 2_000_000,
    maximumStringLength: 1_048_576));
using var output = File.Create("copy.dxf");
raw.Save(output);
```

The raw API is in `netDxf.IO`. Loading reads from the caller's current position
through physical EOF, supports nonseekable input and leaves the stream open.
Input is buffered; byte/tag/string limits are not a total-heap or streaming
guarantee. `WithTags` returns a new snapshot and does not permit an implicit
version change. Raw copying does not repair ownership, resolve external
resources or interpret private application payloads.

Text output rejects embedded CR/LF in individual string values. Binary output
can retain such strings but cannot represent comment group 999. Cross-transport
conversion rejects unsupported values rather than silently dropping them.
Legacy encodings depend on the admitted profile/code page; modern profiles use
strict UTF-8. Hosts may need `CodePagesEncodingProvider` for legacy encodings.
See [raw options](../../netDxf/IO/DxfRawOptions.cs) and
[raw-document tests](../../tests/netDxf.Conformance/RawDocumentTests.cs).

## Numeric values

Text doubles use invariant syntax and round once to the nearest binary64 value,
with ties to an even significand. Signed zero and underflow signs are retained;
values rounding to infinity reject. Thousands separators, nonfinite spellings,
NULs and trailing material are not accepted numeric tokens.

The portable converter serves the Framework/netstandard assets and long tokens
on modern targets. It scans the whole token but bounds integer arithmetic to
the first 1,152 significant digits plus a sticky bit. That prefix exceeds the
decimal precision of every binary64 rounding midpoint, so a discarded nonzero
tail only needs to distinguish a tie from a value above it. The modern fast path
is limited to tokens of at most 64 characters.

The [parser](../../netDxf/IO/DxfDoubleParser.cs),
[shared exact-rounding cases](../../tests/shared/PortableDoubleCases.cs) and
[independent verifier](../../tools/verify_portable_double_parsing.py) define the
edge cases. The [historical derivation](https://github.com/wieslawsoltes/netDxf/blob/8ee6418acea8fb0c91777aeaa68db073317e2421/doc/dxf-conformance/portable-double-parsing.md)
retains the detailed midpoint argument and original qualification.
