# Pinned ordinal globalization profile

The reference profile in [`baseline.json`](../baseline.json) is
`dotnet8-ubuntu22-ordinal-v1`: the selected .NET 8.0.31 comparer on Ubuntu 22.04
Linux x64. Its 1,448 nonidentity scalar mappings have ordered-map SHA-256
`09afae79604b66c70a492180ff3beb17ef36595a8a75db3c2b858f6c4bae609b`.
This pins behavior, not just an operating-system label or SDK version.

The native oracle obtains mappings using `Rune.ToUpperInvariant` and checks the
actual `StringComparer.OrdinalIgnoreCase`. The JavaScript runtime uses the
retained scalar table rather than the host browser/Node Unicode version. It
performs no multi-character uppercase expansion and does not load ICU or .NET.

SDK/runtime pins alone do not fix ICU/NLS behavior. Regression inputs include
U+019B/U+A7DC, U+0264/U+A7CB, U+1C8A/U+1C89, U+A7CD/U+A7CC and U+A7DB/U+A7DA. Do not
remove unequal pairs or silently regenerate the table for a different host.

After building the oracle, run:

```sh
node tools/ordinal-casing.mjs --check
```

The check fails on a different map; a host-profile mismatch is not a passing
comparison or a reason to change the expected profile. Review and explicitly
qualify any new profile. Other platforms and culture-sensitive collation remain
separate acceptance work. See [verification](VERIFICATION.md) and Microsoft's
[globalization documentation](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu).
