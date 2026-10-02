# Windows atomic replacement host

The portable DXF engine is JavaScript. The optional MIT-licensed
`native/windows/atomic_replace.cc` addon invokes `ReplaceFileW` through Node-API;
it contains no DXF engine, .NET bridge, COM or arbitrary native-call interface.
The browser entry does not import it.

## Replacement and loading contract

Existing-file atomic saving needs the replacement primitive corresponding to
C# File.Replace, including held-reader identity behavior. The addon does not close
another caller's reader, copy over a destination or delete the destination first.
New-file publication remains the separate exclusive hard-link strategy described
in [FILESYSTEM.md](FILESYSTEM.md).

Without a built host, existing-file replacement on Windows fails explicitly with
NotSupportedException. Win32 failures retain their code through the adapter's
exception mapping. Invalid/failed loads do not poison the loader cache; validated
callables, not mutable export objects, are retained after successful loading.
These are narrower contracts than all Windows filesystem or System.IO behavior.

## Build and package

On Windows with Visual Studio C++ build tools and the selected Node/npm toolchain:

```sh
npm run build:windows-host
npm run test:windows-host
npm run test:filesystem
npm run test:package
```

The explicit build uses npm's node-gyp and may retrieve matching Node headers.
Module import and package installation do not compile or download the addon.
The Node-API 8 output is `native/bin/win32-<arch>/netdxf_windows.node`, accompanied
by source/binary hash metadata. Generated binaries remain ignored. A pack can
include the built architecture; an unbuilt pack cannot claim replacement support.

Keep the Node entry named `node-entry.js`, exposed as
`@netdxf/javascript/node`. A package-root `node.js` can shadow node.exe during
extensionless Windows npm command lookup; the command-resolution controls protect
against that mistake.

## Qualification

`test:windows-host` requires a real Windows process and the built addon. It covers
native argument guards, Unicode paths, held-reader identity, failed-publication
byte protection and repeated missing-binary calls. Non-Windows execution fails
rather than becoming a successful skipped run.

Supplemental loader tests use injected hosts and do not establish real Windows
behavior. Linux package tests, old CI checkpoints and simulated LF/CRLF output
likewise do not replace fresh Windows evidence. Store logs and source/binary
fingerprints under `artifacts/windows-host/`; full evidence rules are in
[VERIFICATION.md](VERIFICATION.md).

Native API references:
[ReplaceFileW](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew)
and [Node-API](https://nodejs.org/api/n-api.html).
