# Raw atomic filesystem saves

## API and mirrored source

`netDxf/IO/DxfAtomicFile.cs` maps to `javascript/netDxf/IO/DxfAtomicFile.js`.
`netDxf/IO/DxfRawDocument.AtomicSave.cs` maps to its identically named `.js` partial implementation.
The public API remains `document.SaveAtomic(file, binary?, cancellationToken?)` and is synchronous. An AbortSignal or a token with `ThrowIfCancellationRequested()` is accepted by the existing cancellation adapter.

The default package entry imports no Node filesystem module and does not gain filesystem capability implicitly. The explicit `@netdxf/javascript/node` entry registers the host and exports a synchronous `FileStream`. It may be used with existing `Load(stream)` and `Save(stream)` overloads. The latter remain nontransactional; only `SaveAtomic` promises the documented destination-byte protection.

## Save sequence

The implementation validates arguments/cancellation, resolves the path and validates the existing destination before creating any staging file. It creates a random `.netdxf-*.tmp` sibling with exclusive creation, serializes into it, checks cancellation, performs a real file flush, closes the staging handle, rechecks cancellation and destination existence, then publishes. Serialization, invalid transport, budget, cancellation, flush and detected existence-change failures do not publish the staged prefix. A held reader retains the previous file while later opens observe the replacement in the qualified Linux and Windows runs. Windows requires the optional, explicitly built replacement host described below.

Cleanup is attempted on success and failure. IO or access errors during cleanup do not conceal the original error. A cleanup failure may leave an inert sibling; it is not a guarantee that every possible disk failure leaves no temporary file. An asynchronous serializer is rejected instead of reporting success before its work is done.

## Explicit Node adaptation

The host primitives follow the [Node 22.16 filesystem API](https://nodejs.org/download/release/v22.16.0/docs/api/fs.html). Existing files use same-directory `renameSync` on Linux and `ReplaceFileW` through the optional Node-API host on Windows. New files use `linkSync` for exclusive publication, then remove the temporary name. This rejects a destination created between the last existence check and publication rather than overwriting it. It requires a filesystem supporting hard links; failure does not trigger a copy/delete fallback. Once link publication succeeds, a cleanup problem is not reported as failure of destination publication.

Destination symbolic links (including dangling links), directories/nonregular files and read-only files are rejected. Symlinks or replacement of parent directories, hostile concurrent namespace changes, concurrent writers changing an already-existing destination, metadata/ACL preservation and caller-selected sharing modes are not fully modeled. The API is not a filesystem isolation transaction. It does not promise directory-fsync/power-loss durability or equivalence of every System.IO overload/platform.

The small host stream supports Open/CreateNew/Create with Read/Write/ReadWrite, caller-owned lifetime, bounded buffer slices, partial-write draining, byte IO, Position/Length, SetLength and Flush. Positions must be exactly representable safe JavaScript integers. Additional FileMode/FileShare/FileOptions overloads, asynchronous IO and browser file handles remain outside this adapter.

## Evidence and limits

`AtomicSaveTests.js` mirrors 82 original raw/internal-helper case identities: exact saves and invalid conversions across all nine raw version families and both transports, failures after staged writes, cancellation/disposal/race detection, held-reader visibility and output-budget protection. Original tests that construct typed DxfDocument remain unported.

The separate actual .NET filesystem oracle compares 1,782 scenarios, including all 399 original DXF files, both requested output transports, new/existing destinations, path failures, readonly files, destination links, cancellation, flush/serialization failures and held-reader content. The exact output comparator is unchanged: no timestamp stripping, numeric tolerance, handle renumbering or semantic normalization is applied.

Supplemental tests inject short/zero writes, fsync failure, host-publication failure, unsupported hard-link publication, a destination race at publication, cleanup failure, host reentrancy and incorrect asynchronous serialization. These are not counted as additional original .NET cases. Results are recorded under `artifacts/filesystem-differential/<configuration>/results.json`; platform-specific CI artifacts are authoritative for Linux and Windows. Passing these tests does not establish full filesystem or library parity.

Run with the pinned SDK and source checkout:

```sh
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
npm run test:filesystem
DXF_TEST_FILTER=atomic/ npm test
```

No release or full-port gate is relaxed by adding this host capability.

## Windows held-reader failure and resolution

CI at `4fb1c72` ran the unchanged original `atomic/reader-observes-old-or-new` case on Windows Server 2022 with Node 22.16.0. The other 81 raw/helper cases passed, but replacing the file with a held reader failed with `EPERM`, mapped to `UnauthorizedAccessException`. **That earlier checkpoint did not qualify Windows replacement.** This is not a permitted skip, a changed expected result, or a reason to mark the full-port gate green.

The pinned [Node/libuv Windows rename implementation](https://github.com/nodejs/node/blob/v22.16.0/deps/uv/src/win/fs.c#L2079) calls `MoveFileExW` with `MOVEFILE_REPLACE_EXISTING`. The C# helper in `netDxf/IO/DxfAtomicFile.cs` instead calls `File.Replace` for existing destinations. The replacement host now uses `ReplaceFileW` instead; see [the Windows build and runtime contract](WINDOWS_HOST.md). An in-place copy, delete-and-rename sequence, or closing another caller's reader would violate the tested atomicity/ownership contract and is deliberately not used as a fallback.

The Windows CI job continues the full 1,782-scenario differential and offline package checks even when an original case fails, retaining negative as well as positive evidence. The filesystem differential holds a reader for every existing-destination scenario; it therefore tests more than the one named original held-reader case. Its report, not the initial 81/82 count, gives the total cross-runtime mismatch count. Linux and Windows evidence must not be conflated.

The run at `2ee0817c103e1d1993b7de4e643d804bf8af1b42` independently passed all
82 original raw/helper cases and all 1,782 filesystem comparisons on Windows
Server 2022 x64 and Ubuntu 22.04. There were 1,686 exact byte comparisons per
platform. The Windows addon build and offline packed-package held-reader test
also passed. See [run 35200920200](https://github.com/wieslawsoltes/netDxf/actions/runs/35200920200).
This resolves the previously reported 793 Windows filesystem mismatches for that
corpus, not every System.IO or DXF API. Exact numerical and full-port completion
gates remain failing; no tolerance, allowlist, or destructive fallback was added.

The loader-hardening checkpoint `9b10188e181e87bc963ea145ea43f1e348e2934d`
repeated all 82 original raw/helper cases and all 1,782 differential comparisons
successfully on both platforms. Its Windows job additionally passed five actual
native-host integration tests and all 93 supplemental tests. The injected
publication-failure test now targets the filesystem adapter, so it covers the
Windows replacement path as well as Linux rename. See
[run 35206647072](https://github.com/wieslawsoltes/netDxf/actions/runs/35206647072).
