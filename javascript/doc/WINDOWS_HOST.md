# Windows atomic replacement host

The portable DXF engine remains native JavaScript. The optional Windows filesystem
host is a narrow Node-API addon invoking `ReplaceFileW`. It does not contain DXF
logic, a .NET runtime, COM, shell execution, or an arbitrary native-call interface.
The browser entry does not import it. Linux uses its existing rename implementation.

## Why a host bridge is necessary

The pinned .NET implementation invokes `File.Replace` for an existing destination.
The prior Node implementation used `fs.renameSync`/libuv `MoveFileExW`, which failed
while a delete-sharing reader was open on Windows. The bridge uses the same
replacement primitive as .NET and leaves existing reader handles open on the old
file identity. It does not close readers, copy over the destination, delete the
old destination first, or hide a failed replacement.

Source: `native/windows/atomic_replace.cc` (MIT). Win32 contract:
https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew
Node-API contract: https://nodejs.org/api/n-api.html

## Build and package

On Windows with Visual Studio C++ build tools and the pinned Node/npm toolchain:

```sh
npm run build:windows-host
npm run test:filesystem
npm run test:package
```

The explicit build command uses node-gyp bundled with npm; its normal build may
retrieve the matching Node headers. Nothing is compiled or downloaded during
module import or package installation. The addon targets Node-API 8. The build
writes `native/bin/win32-<arch>/netdxf_windows.node` plus a source/binary hash report.
Those binaries are not committed to Git. A Windows-produced npm pack includes its
built architecture; an unbuilt package cannot claim Windows replacement support.

Loading the portable module does not require a binary. Attempting existing-file
atomic replacement on Windows without the bridge fails explicitly with
`NotSupportedException`, leaving the destination untouched. Native errors retain
`Win32ErrorCode` and map to the existing exception classes. This adapter is not
complete System.IO.FileStream or arbitrary Windows filesystem parity.

CI builds the bridge on Windows and executes the existing 82 raw/helper cases and
all 1,782 filesystem differential comparisons without an exclusion or allowlist.
Verification results are recorded separately; adding the adapter is not by itself
a Windows qualification pass. New-file publication still uses the existing
exclusive hard-link strategy.

## Native-host regression evidence

`npm run test:windows-host` requires a real Windows process and the built addon.
It checks native arity/type/NUL/length guards, JS argument validation, Unicode
filenames with held-reader identity preservation, missing-source byte protection,
and two consecutive calls from an installation without a native binary. It writes
`artifacts/windows-host/results.json` and the complete TAP log; five successful
executed cases, matching source/binary hashes, and unchanged runtime/verifier
fingerprints are required. A non-Windows invocation fails rather than skipping.

The portable supplemental suite separately tests the loader contract. Failed or
invalid loads do not poison its cache, successful loading retains the validated
callable rather than a mutable export object, malformed statuses are errors, and
native failures retain their original Win32 code. Those simulated error-path tests
are not counted as Windows integration or original C# test cases.

The package's Node entry is `node-entry.js`, still exposed as
`@netdxf/javascript/node`. Do not rename it to `node.js`: on Windows, a package-root
file with that name can shadow `node.exe` during extensionless npm shell lookup.
The command-resolution regressions execute from the package root.

## Executed checkpoint

At `9b10188e181e87bc963ea145ea43f1e348e2934d`, the Windows Server 2022 x64 job
passed the native build, all five real addon tests, all 93 supplemental tests,
all 82 original raw/helper atomic-save cases, all 1,782 filesystem differential
comparisons (1,686 exact byte comparisons), and offline packed-package testing.
There were no skipped or TODO tests in either added test suite. The Windows pack
contained 176 files, including its built host and build metadata. See
[run 35206647072](https://github.com/wieslawsoltes/netDxf/actions/runs/35206647072).

This is Windows filesystem evidence only: the exact foundation, randomized
geometry, Debug NaN-sign and full-port completion gates still report failures.
The host binary is not used to implement geometry or DXF processing.

The downloaded Windows artifact has SHA-256
`73b1c97a025ff0f0bd2ddcffbce8d140dfaf0a82b5e89edbb86e3029803f97f1`.
Its runtime fingerprint is
`b584a41ddfdc4e2274608ab85c0d37a1ce0a36711cc208e88bf91ca897fc881c`.
The verifier fingerprints are platform-specific because the current recipe sorts
native file paths before normalizing separators: Windows
`1332d1698af4c9a61e077f8eccf2bf1d28af75c4dfbf4d430f4ad4f820dd7659`,
POSIX `00d4e1f8e49179fbdfe1376cefe8dba469ac1d42f2c22742ee5ab08e8ec1ec4f`.
Both were reproduced from the same committed file bytes using each platform's
ordering. They must not be mistaken for identical cross-platform digest strings.
