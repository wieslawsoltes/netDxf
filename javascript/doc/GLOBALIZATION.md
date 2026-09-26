# Pinned ordinal globalization profile

The reference is **`dotnet8-ubuntu22-ordinal-v1`**: the actual .NET 8.0.31 production comparer on the Ubuntu 22.04 Linux x64 CI platform. `baseline.json` records its 1,448 nonidentity scalar mappings and the SHA-256 of their ordered JSON representation:

`09afae79604b66c70a492180ff3beb17ef36595a8a75db3c2b858f6c4bae609b`

This is a behavioral pin, not a replacement implementation of the .NET oracle. The oracle obtains each mapping from `Rune.ToUpperInvariant` and confirms it using the actual `StringComparer.OrdinalIgnoreCase`. The verifier hashes the complete returned map and rejects a different host profile before running the full CI suite. Generation also refuses to silently replace the selected profile.

## Why the SDK and runtime pins were insufficient

The first expanded CI run found ten directional comparison mismatches involving five newer Unicode case pairs, despite matching SDK and runtime versions. The local container used ICU 76, and the reference runner's comparer did not equate these pairs. Raw, handle, and OBJECTS exact-output tests passed in that run; the separate casing gate correctly failed instead of concealing the difference.

The JavaScript table now targets the reference comparer rather than automatically adopting the browser/Node host's Unicode additions. Regression tests include U+019B/U+A7DC, U+0264/U+A7CB, U+1C8A/U+1C89, U+A7CD/U+A7CC, and U+A7DB/U+A7DA. All original scalar comparisons, including those unequal pairs, remain in the differential corpus. No comparison is skipped or normalized away.

## Running the oracle

Run `node tools/ordinal-casing.mjs --check` after building the oracle. CI does this before the complete conformance suite. A machine with a different ICU/NLS/invariant globalization profile must not claim the reference evidence; use the documented CI environment or explicitly qualify and review a new profile. A mismatch prints the actual count/hash and fails. It does not change the JavaScript table or .NET behavior to make the test pass.

The shipped JavaScript contains only the native scalar lookup table. It does not load ICU, .NET, a native binary, or a service at runtime. Other .NET globalization profiles remain outside current qualification; full-port platform parity remains an incomplete gate.

See Microsoft's [.NET globalization and ICU documentation](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu) for the runtime's platform/globalization dependencies. The executable profile check, not an assumption about an operating-system label, determines whether a reference run is comparable.
