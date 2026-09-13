# Preserve finite binary64 values in text DXF output

Baseline: `8914abf54a6c10ee06270051538625665ef13d48`, after merged PR #26.

## Reproduced loss

The fixed custom numeric format `0.0###############` was not a round-trip representation. The actual production writer emitted `1e-20` and the smallest positive subnormal as `0.0`; it changed `1.2345678901234567` to `1.23456789012346`; and its rounded spelling of `double.MaxValue` parsed as positive infinity. The strict reader then rejected that formerly finite value. This was numerical data loss, not simply display precision.

Use invariant `G17` formatting for the full finite-double precision and exponent range. Explicitly emit positive/negative zero as `0.0`/`-0.0`, and retain a `.0` suffix on non-exponential integer-valued reals. `G17` was selected rather than `R` because the project retains .NET Framework targets and Microsoft documents round-trip limitations of `R` there.

The change affects all text-codec double groups, and therefore scalar geometry, header values, XData reals and other double-valued fields. Binary double encoding is unchanged. The public API, target frameworks, signing, culture behavior and non-finite writer behavior remain unchanged. General writer validation and transactional output are separate work. A geometry algorithm may still change values before serialization; this fix does not make every geometric transformation bit-exact.

## Coverage by admitted version

| Path | 2000 / AC1015 | 2004 / AC1018 | 2007 / AC1021 | 2010 / AC1024 | 2013 / AC1027 | 2018 / AC1032 |
|---|---|---|---|---|---|---|
| POINT / XData / custom HEADER doubles | Exact-bit tests | Exact-bit tests | Exact-bit tests | Exact-bit tests | Exact-bit tests | Exact-bit tests |
| Alternating text and binary round trips | Tested | Tested | Tested | Tested | Tested | Tested |
| Independent ezdxf fixture comparison | Exact bits | Exact bits | Exact bits | Exact bits | Exact bits | Exact bits |

Every existing double code is exercised in both transports with 24 boundary/precision values. The deterministic randomized test adds 4,096 finite bit patterns in each of four cultures. Signed zeros, minimum subnormals, normal/subnormal boundaries, adjacent values around one, maximum magnitude, small coordinates, and 53-bit integer boundaries are represented. Bit comparisons replace approximate equality for this contract.

377 new registered cases. Old writer: **4,730 passed / 196 failed**. Corrected writer: **4,926 passed / 0 failed**, local Debug and Release. Final-head Linux/Windows SDK, netstandard2.0 and source audit are required merge gates. Runtime execution evidence here uses .NET 8; older target compilation alone is not proof of identical older-runtime parsing behavior.

Independent ezdxf 1.4.4 verification reads all twelve retained files and compares the IEEE 754 bits of all 24 POINT X coordinates, their XData reals, and a small custom header value. All match with **zero audit errors and zero repairs**. No AutoCAD process was executed.

## Compatibility and remaining work

Text spellings can change, including longer significant-digit mantissas and scientific notation for extreme magnitudes. Byte/snapshot comparisons of old output must not be confused with numeric equality. No pre-2000 document dialect, user-selectable lossy precision reduction, custom float parser for older runtimes, non-finite writer validation, or generic unknown-record preservation is introduced.

## Primary references

- Autodesk group value types (double-precision fields): https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-2553CF98-44F6-4828-82DD-FE3BC7448113.htm
- Microsoft general and round-trip numeric formats, including G17 on .NET Framework: https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings#general-format-specifier-g
