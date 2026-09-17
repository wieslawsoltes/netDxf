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
