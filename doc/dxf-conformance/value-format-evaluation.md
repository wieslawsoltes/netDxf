# Scalar value-format evaluation

`netDxf.Units.DxfValueFormat` compiles a decoded format expression into an
immutable scalar evaluator. `DxfStoredTableContentValue` extension methods can
calculate display text and create same-kind scalar edits; `RefreshFormattedText`
refreshes selected display strings through the existing atomic content edit.
These APIs do not execute fields, table formulas, MText, file paths or arbitrary
conversion callbacks.

## Supported controls and explicit choices

| Control | Contract |
|---|---|
| `%lu1` through `%lu5` | Scientific, decimal, engineering, architectural and fractional plain text |
| `%pr0` through `%pr8` | Explicit decimal precision, or binary-fraction denominator exponent |
| `%ps[prefix,suffix]` | Literal affixes, exactly one comma; percent signs inside the brackets remain data |
| `%ct8[factor]` | One finite invariant-culture IEEE double multiplier |
| `%ds44`, `%ds46` | Comma or period decimal separator |
| `%th0`, `%th32`, `%th44`, `%th46` | No grouping, space, comma or period thousands separator |
| `%zs0`, `%zs4`, `%zs8`, `%zs12` | No suppression, leading decimal zero, trailing decimal zeros, or both |

Numeric controls require both `%lu` and `%pr`; drawing defaults are not inferred.
The decimal and thousands separators must differ. Fractional/architectural
modes reject decimal separator and zero-suppression controls. Duplicate controls,
nested brackets, unknown options and malformed expressions reject rather than
being silently ignored. Empty expressions use invariant round-trip numeric
text or unchanged string data, normalizing signed zero to `0`. This explicit
fallback is not an inference of an inherited TABLESTYLE or document setting.

The accepted CLR values are exactly `int`, finite `double` and `string`.
Strings do not undergo numeric coercion. Supported unit codes are unitless,
distance, area, volume, currency and percentage (0, 1, 4, 8, 16, 32). Angular,
compound and unknown codes reject. Percentage values are not implicitly scaled
by 100: the native scalar `35` with `%lu2%pr2%ps[,%]` displays `35.00%`.

Conversion uses one double multiplication and rejects a nonfinite result.
After conversion, fixed, scientific and fractional rounding uses the exact
binary rational represented by the double, with midpoint ties away from zero.
This differs from treating the input as an ideal decimal number: for example,
a binary64 value close to a decimal midpoint may fall on either side of it.
BigInteger arithmetic avoids fixed-width integer overflow across the binary64
range, including subnormals. Feet/inches carry is resolved after rounding, and
rounded zero does not keep a negative sign. Fractions are reduced plain text,
not font-dependent stacked MText. Scientific exponents include a sign and at
least two digits. Results are independent of the process culture.

Expressions and results are bounded by 4,096 UTF-16 code units. NUL and unpaired
surrogates reject. Literal CR/LF remain text; ordinary DXF text/binary save rules
still decide whether that text can be serialized in the chosen transport.

## Source evidence and qualification distinction

Autodesk's [String Conversions documentation](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-DevGuide/files/GUID-29D02F6C-AFF2-433B-8F6D-8E9D7F1F6758.htm)
provides the five linear display examples for `17.5`. The
[UnitType enumeration](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_UnitType.html)
identifies the unit codes. Autodesk's
[Additional Format dialog](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT/files/GUID-D1EA1BF5-6442-47EF-82F5-D6849A07906E.htm)
documents the corresponding conversion, affix, separator and suppression options.
The [field-format example by its author](https://www.lee-mac.com/fieldformat.html)
contains `%lu2%pr2%ct8[1000]%th44`.

The pinned native AC1021 and AC1024 TABLECONTENT packets provide direct stored
scalar/expression/display examples for currency and percentage. Tests compare
the evaluator with those producer display values. Other edge cases establish
the declared deterministic implementation contract, not exhaustive native
AutoCAD equivalence. Native execution is still required to qualify all rounding,
UNITMODE, DIMZIN, locale, inherited-format and private-control behavior.

## Stored-content integration

```csharp
using netDxf.Objects;
using netDxf.Units;

string display = DxfValueFormat.Parse("%lu2%pr2%ps[$,]").Format(100.0, 16);
// $100.00

DxfStoredTableContentValue value = content.StoredValues[0];
string preview = value.EvaluateFormattedText();
content.RefreshFormattedText(new[] { value });

// Replacement must retain the actual scalar's CLR kind. This example is for int.
content.ReplaceContent(content.Name, content.Description, content.TableStyle,
    new[] { content.StoredValues[0].WithEvaluatedValue(123) });
```

`EvaluateFormattedText` reads only the selected value's own expression and unit
code. `WithEvaluatedValue` validates the exact scalar kind before evaluation and
returns an ordinary immutable value edit. `RefreshFormattedText` materializes
and disposes requests inside `ReplaceContent`'s existing guard. Unsupported
values, duplicate/foreign/stale snapshots, failed disposal, caught reentry and
source-profile changes abort the operation without partially publishing display
strings. The guard resets after rejection. Independent changes made by caller
callbacks to other document state are not rolled back.

Refresh retains scalar values, unit/format flags, expressions, dependencies,
common metadata and every unselected field. Earlier snapshots remain unchanged;
equivalent refreshes retain the current snapshots. No handles are allocated.
The compact R2004 scalar representation has no independent expression/display
fields and explicitly rejects these convenience operations. Existing explicit
`WithValue(value, formattedText)` editing is unchanged.

**TABLE inline caches, display blocks, geometry and other objects are not
regenerated by this API.** Refreshing a backing TABLECONTENT object's display
is not advertised as full visual regeneration of its TABLE consumer.

## Verification

The initial `value-format/` group adds 89 conformance cases. One case emits a
4,500-row matrix: 100 pinned edge/seeded-random finite IEEE values, five modes
and nine precisions. These 4,500 evaluations are not counted as separate harness
cases. Native tests reproduce currency/percentage output; culture tests cover
four process cultures; content tests cover R2007–R2018 and both transports with
failure, snapshot, reentry and resource checks.

`tools/verify_value_formats.py` independently computes expected numeric text
using `Decimal.from_float` at 1,200-digit precision and `ROUND_HALF_UP`, not the
production BigInteger implementation. It verifies the complete mode/precision
cross product and pinned input-bit inventory. The same numeric validator rejects
each deliberately corrupted actual output. Eight DXF before/after pairs compare
all ordered physical records, allowing only the three requested display strings
to change. The existing exact-empty ACAD_LAYERSTATES identity normalization is
retained; HEADER time/seed and CLASS records are outside that physical-record
comparison. Every content field and every unrelated record is separately
corrupted and must be rejected. Eight native-family outputs retain the pinned
producer value/unit/expression/display packets and profiles.

Against the implementation artifact the new verifier passes 4,500 numerical
results, eight complete-record refresh pairs and eight native numeric examples,
rejecting 5,508 corruptions. Final-head results and artifact digests are recorded
in PR #101. These checks supplement the historical PR95 coverage ledger; they do
not promote broad DXF families to complete or replace native AutoCAD execution.

```sh
DXF_TEST_FILTER=value-format/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_value_formats.py artifacts/conformance
```
