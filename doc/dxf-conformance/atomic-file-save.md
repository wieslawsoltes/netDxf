# Explicit atomic file saves

> Merged as [PR #70](https://github.com/wieslawsoltes/netDxf/pull/70). Final-head [CI 34861184112](https://github.com/wieslawsoltes/netDxf/actions/runs/34861184112) passed Linux/Windows Debug/Release, actual netstandard2.0 builds, ledger checks and the Linux source audit; the retained Linux Debug report has **16,075 passed / zero failed**. The evidence section below retains the original standalone local experiment, not the later accumulated suite count. See the [merged execution checkpoint](checkpoint-merged-2026-09-14.md) for source hashes and qualification limits.

## API and contract

`DxfDocument.SaveAtomic(path, isBinary, cancellationToken)` and the two
`DxfRawDocument.SaveAtomic` file overloads opt into staged filesystem replacement.
The old `Save` overloads and their Debug/Release result conventions are unchanged.
The new API throws on failures in both configurations: returning success before
commit, suppressing errors, or deleting the destination as a fallback is forbidden.

A uniquely named `CreateNew` temporary file is created beside the destination.
Serialization completes, cancellation is checked, data is flushed with `Flush(true)`,
and the stream is closed. One `File.Replace` commits an existing destination;
`File.Move` commits a previously absent destination. Unsupported replacement fails
rather than degrading into delete-and-copy. Staging cleanup is best effort and
never masks the primary exception.

Existing directories, read-only files and destination symbolic links/reparse points
are rejected. Destination existence is checked before and after staging; appearance
or disappearance aborts. This is NOT hostile-filesystem TOCTOU protection, concurrent
writer isolation, directory-fsync/power-loss certification, preservation of inode or
all metadata identity, or a guarantee for every network/virtual filesystem. Already
open reader behavior is determined by filesystem/platform sharing semantics.
Cancellation after the final checkpoint cannot roll back an already committed rename.

The typed name/working-folder changes are restored on failure. Other existing writer
mutations, including handle/application/layout work, are NOT a document transaction.
Failure does not destroy the previous destination bytes. Raw same-transport saving
retains the exact original file; cross-transport saving keeps the existing codec
restrictions, including binary comment rejection. No new DXF schema is admitted.

## Profile and test evidence

Typed: all six AC1015–AC1032 families, both transports, existing/absent files.
Raw: all nine AC1009–AC1032 admitted families, both transports, existing/absent files.
These are transport/file-lifetime claims, not certificates for every record schema.

156 added cases cover successful replacement and creation, typed and raw failures,
pre-cancellation and staged cancellation, injected serialization failures, cleanup,
file lifetime, destination path errors, symbolic links/read-only files and old-or-new
reader observations. A partial-class static initialization bug and legacy fixture
version names in the recovered test source were corrected before final validation.

Complete actual signed-library .NET 8 results: **15,748 passed / zero failed**, Debug
and Release. New APIs cannot be compiled against the baseline assembly; no red count
is manufactured. The independent verifier checks 24 emitted typed drawings, exact
LINE coordinates and zero ezdxf audit errors/repairs in each configuration. This does
not independently prove filesystem atomicity on other operating systems.

Base: merged PR #68, `cf533ba32d6c732e475192ee021b78f938eda0cb`.
This original local experiment did not execute SDK/MSBuild or Windows; the merged PR subsequently passed those CI gates as recorded above. Native AutoCAD remains unexecuted.

```csharp
document.SaveAtomic("drawing.dxf", isBinary: false, cancellationToken: token);
rawDocument.SaveAtomic("preserved.dxf", cancellationToken: token);
rawDocument.SaveAtomic("converted.dxf", binary: true, cancellationToken: token);
```

Microsoft API contracts: File.Replace, File.Move and FileStream.Flush(Boolean).
The implementation exposes their failure semantics; it does not assume that every
filesystem implements the same durability or metadata behavior.
