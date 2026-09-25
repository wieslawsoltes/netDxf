# Portable DXF decimal-to-binary64 rounding

This extends #209's signed-zero correction to general invariant decimal parsing
on the net471, net48 and netstandard2.0 library assets. The preceding reader used
`Double.TryParse` on every runtime. Microsoft's pre-.NET Core 3 implementations
have documented rounding differences; retaining a zero sign alone does not fix
those differences for arbitrary numeric input.

## Contract

The shared text codec uses a portable integer-based converter on these three
assets. On net6.0/net8.0, only tokens of at most 64 characters use the modern
runtime fast path; longer tokens use the portable converter too. This retains
the existing runtime path for ordinary G17 numbers without delegating arbitrarily
long literals or compensated exponents to runtime-specific behavior. The portable
implementation is compiled on modern targets so tests can compare both selected
and exact paths in the same process. Public signatures, other scalar readers,
binary transport, G17 writing, entity admission and version eligibility remain
unchanged. System.Numerics is an explicit framework reference for net471/net48;
no third-party package dependency is introduced.

Accepted syntax is invariant NumberStyles.Float: optional ASCII edge whitespace
(space and U+0009 through U+000D), optional sign, decimal digits with an optional
dot, and an optional signed decimal exponent. A mantissa and any exponent must
contain digits. Thousands separators, internal whitespace, Unicode numerals,
NULs, nonfinite spellings and trailing material reject. The enclosing DXF reader
still frames values by lines; parser acceptance of edge CR/LF is not permission
to embed a newline in one DXF value. Existing FormatException diagnostics remain.

Conversion rounds once to the nearest binary64 value, resolving exact ties to
an even significand. This includes zero/subnormal, subnormal/normal, binade and
maximum-finite boundaries. Negative exact and underflow zeros retain their sign.
A finite decimal just above Double.MaxValue may round to Double.MaxValue;
anything rounding to infinity rejects. There is no saturation to max-finite.
The old Framework parser's different overflow/rounding decisions are explicitly
not preserved as compatibility behavior.

## Bounded conversion

The complete token is scanned, including after an obvious zero or huge exponent.
The exponent saturates at magnitude 10,000,000,000, larger than a CLR string can
compensate with mantissa digits. Only the first 1,152 significant mantissa digits
are accumulated, in nine-digit chunks; a sticky bit records a nonzero tail.
The input string still requires O(length) scan/storage in the existing reader,
but auxiliary integer arithmetic does not grow with arbitrary token length.
No broader file-size or denial-of-service qualification is claimed.

Why the retained prefix suffices: every positive binary64 rounding midpoint can
be written m*2^e, where m < 2^54 and e >= -1075. For e < 0 its terminating decimal
coefficient is m*5^(-e), which is less than 10^1129; positive exponents produce
at most 309-digit integers. Thus every midpoint has fewer than 1,129 significant
digits. A 1,152-digit prefix cannot skip a rounding boundary; a discarded tail
can affect rounding only when that prefix equals a midpoint. The sticky bit
then selects the upper magnitude instead of the exact-tie even result.

The bounded coefficient and decimal power form an exact integer ratio. Its
binary exponent is obtained from integer bit lengths and comparison. DivRem
at the normal or subnormal quantum provides the candidate significand and
remainder; a single integer halfway comparison decides rounding. Raw bit
construction avoids an intermediate floating-point conversion and double
rounding. Sign is attached after rounding the magnitude.

## Qualification design

The input-only corpus has 12,412 cases: 12,282 below/at/above midpoint cases
covering every finite exponent field and both signs; 66 extra edge cases;
24 delayed-tail cases beyond the retained-digit bound; 14 explicit valid
literals; and 26 invalid tokens. Inputs include 100,000-digit balanced values,
large exponents, signed zero, minimum subnormals, near-one ties and overflow.
Expected midpoint bits derive from neighboring binary64 integers, not from
netDxf output or a captured parser result.

One source file supplies the full corpus to conformance, ordinary installed
NuGet consumption and all eight exact-asset/runtime profiles. Each observation
checks the portable converter, selected converter and actual shared DXF text
codec, including the following EOF record. Two additional conformance cases
cover all declared double groups and culture/edge-whitespace behavior. The shared
package/conformance dispatch checks also cover lengths 63, 64 and 65 with exact
ties, valid/invalid tokens, signed zero/underflow and overflow. Twelve
version/transport cases independently inject midpoint decimal text into valid
POINT records and check typed loading, clones, handle preservation, source
bytes/stream lifetime, following LINE geometry and two resaves. They generate
36 drawings / 936 POINT records for a second independent packet/ezdxf verifier.
The same twelve matrices also run inside installed-package assertions, without
changing the existing twelve smoke-scenario receipt count. Earlier
#209 signed-zero and all other conformance assertions remain unchanged.

The independent Python verifier constructs exact adjacent-value fractions,
checks the expected answers against CPython's separate converter, requires exact
input and observation inventories, then checks the actual exported C# results.
It rejects altered bits, invalid acceptance, tokens, IDs, missing/extra/duplicate
records and duplicate JSON keys. Constructed checker self-tests are not C#
execution evidence. Actual run counts and hosted qualification belong in the
PR evidence; definitions here do not imply any suite has run.

The existing two workflows and all previous gates remain. Full conformance is
.NET8; package profiles exercise the shared cases on selected assemblies, not
the entire conformance suite on every CLR. Windows Framework updates are
in-place and do not prove independent original CLR installations.

## Initial hosted regression and correction

The initial .NET8 Linux Release artifact for PR #210 head
`2f8bd7851f45b80fa2eb33e8fe49a53414bab0c1` (run `36116048494`, artifact
`10855427608`, SHA-256
`3424c8cf16c61b82a51ea74d42183adcaae6669db1e127fa33505f26d4796ea8`)
contains 94,556 results: 94,033 pass and 523 fail. All failures are newly added
cases: 511 selected-runtime long-literal results and 12 consequent typed source
loads. The portable assertion runs first and passed every one of the 12,412
input cases. The exported drawings contain only the 12 source files, not the
36 required source/output/resave files. This is failed qualification, regardless
of any conflicting run summary or job-log text. No old case was removed.

The correction selects the portable converter for long tokens on modern targets
as well, adds threshold checks to conformance and installed-package tests, and
retains failing observations before assertions with expected/actual-bit
diagnostics. The initial report omitted failed observations and must not be
accepted by the independent inventory check. The corrected candidate needs its
own complete hosted qualification; this paragraph does not claim it has passed.

## Boundaries and references

No native AutoCAD open/AUDIT/save/reopen, native producer lexical policy,
historical typed/pre-R11 dialect, arbitrary entity semantics, FIELD/TABLE/private
cache regeneration, dependency-complete import or general version conversion is
qualified by this parser change. Source numeric spelling is not preserved.
The JavaScript port remains separate. Full AutoCAD parity is not established.

Primary references:
- Autodesk group-code value types (double-precision numeric groups):
  https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-2553CF98-44F6-4828-82DD-FE3BC7448113.htm
- Microsoft, floating-point parsing improvements and legacy differences:
  https://devblogs.microsoft.com/dotnet/floating-point-parsing-and-formatting-improvements-in-net-core-3-0/
- Microsoft NumberStyles.Float grammar and the restricted whitespace set:
  https://learn.microsoft.com/en-us/dotnet/api/system.globalization.numberstyles
