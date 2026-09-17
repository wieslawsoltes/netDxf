# Explicit standard FIELD evaluation

This module follows merged PR #105, source tree
`c127fd8d1f4f25ca81aea529e968927d1fd066b3`. It preserves binary results,
failure outcomes and multi-tree transactions. The original uploaded patch was
recovered exactly before correcting its stale unit-helper calls and independent
checker invocation. It supplies an opt-in bounded evaluator, not a universal
native-expression engine. Qualification of the final source is recorded in PR #106.

## Supported expressions and explicit inputs

`DxfStandardFieldEvaluator` handles the exact evaluator IDs `_text`, `AcVar` and
`AcExpr`. `_text` delegates to the existing qualified child-slot composer.
`AcVar` reads a copied dictionary of explicitly supplied `DxfFieldVariable`
objects. Names are case-sensitive ASCII identifiers; case-insensitive drawing
variable lookup is not inferred. `AcExpr` uses bounded arithmetic, literal
aggregates and numeric values from explicitly owned child-index slots.

```csharp
using netDxf.Objects;

var evaluator = new DxfStandardFieldEvaluator(
    new Dictionary<string, DxfFieldVariable>
    {
        ["Amount"] = new DxfFieldVariable(21.0),
        ["Angle"] = new DxfFieldVariable(Math.PI / 2, storedUnitType: 2),
        ["Date"] = DxfFieldVariable.FromDateTime(suppliedClock)
    });

// Persist success or an explicit retained-cache failure for each field.
int changed = document.Objects.EvaluateFieldTrees(roots, evaluator.Evaluate);

// Alternatively, any evaluator error aborts the entire selected forest.
int allOrNothing = document.Objects.EvaluateFieldTrees(roots, evaluator.EvaluateOrThrow);
```

Example accepted code, supplied in an existing qualified FIELD:

```text
\AcVar Amount \f "%lu2%pr2"
\AcVar Date \f "yyyy-MM-dd HH:mm:ss"
\AcVar Angle \f "%au0%pr2"
\AcExpr (2^3 + 6/4) \f "%lu2%pr3"
\AcExpr (%<\_FldIdx 0>% * 2) \f "%lu2%pr2"
```

A complete outer `%< ... >%` wrapper is optional. An explicit `\f "..."` selects
the decoded format; otherwise the retained cache-format string is used. Quoting
must be complete and followed only by spaces or tabs. Multiple/trailing switches,
unknown commands and unqualified nested FIELD controls reject. Double quotes
inside the format string are not escaped by this grammar; date-mask literals
can instead use single quotes. No native-expression AST is written back.

Bindings admit null, Int32, finite double and bounded string values. Numeric
unit codes are 0 (unitless), 1 (distance), 2 (angle in radians), 4 (area), 8
(volume), 16 (currency) and 32 (percentage). Strings and null have no numeric
units. There is no lookup of current time, files, environment variables, object
handles, sheet sets or external services. Strings beginning with `=` or FIELD
markers remain strings. Resource/reference graphs are not imported.

## Scalar expressions and child dependencies

`DxfTableFormula.ParseScalar` reuses the existing arithmetic compiler but rejects
cell references while parsing. `EvaluateScalar` accepts only a formula compiled
in that mode. It never calls a table-cell resolver. Existing table-formula
behavior is unchanged. Supported arithmetic is `+ - * / ^`, unary signs,
parentheses, invariant finite numbers, and the existing literal aggregate
functions SUM, AVERAGE, COUNT, MIN and MAX. Cell/range references, unknown
functions, divide-by-zero and nonfinite results reject.

Owned `%<\_FldIdx N>%` references use the detached child result's numeric scalar,
not its formatted display. Substituted values are parenthesized, preserving
negative-operand precedence. Child text, binary values, null, failed outcomes
and invalid indices cannot become implicit numeric operands. Child display text
is never reparsed as code. Raw scalar arithmetic does not perform automatic
cross-unit conversion or infer date arithmetic semantics.

The existing FIELD forest owns dependency ordering, projection checks, context
admission, stale-snapshot checks and publication. This provider does not make
native/private object references into additional expression dependencies. Its
explicit bindings have at most 4,096 names and 4,194,304 combined name/value text
units. Each code, date mask, binding string, expanded expression and rendered
value has the applicable existing bounded limit (4,096 units for this provider,
including the scalar expression's leading equals sign). Existing calculation
operation, expression-depth and FIELD transaction limits remain active.

## Date and time formats

`DxfDateTimeFormat.Parse(mask, culture)` copies and freezes the culture, invariant
by default, and requires a Gregorian calendar. Formatting uses supplied clock
fields and does not convert DateTime.Kind into a timezone offset. Supported
explicit token policies are:

| Token | Output |
|---|---|
| d / dd | Day of month, unpadded / two digits |
| ddd / dddd | Abbreviated / full weekday name from the frozen culture |
| M / MM / MMM / MMMM | Month number / two digits / abbreviated / full name |
| y / yy | Last two year digits, unpadded / padded |
| yyy / yyyy | Four-digit Gregorian year, including leading zeros |
| h / hh | Twelve-hour clock, unpadded / padded |
| H / HH | Twenty-four-hour clock, unpadded / padded |
| m / mm, s / ss | Minutes and seconds |
| t / tt | First text element / complete AM-PM designator |

The `yyy` four-digit behavior is an explicit contract, not .NET's minimum-three-
digit custom formatting. Unsupported token repetitions and unknown unquoted ASCII
letters reject. Quotes delimit literals, doubled matching quotes produce a quote,
and a backslash escapes the following character. Unsupported fractional-second,
timezone and calendar controls are not silently ignored.

Regional `%c` uses the copied short-date plus long-time patterns, `%#c` the long-
date plus long-time patterns, `%x` the short-date pattern, `%#x` the long-date
pattern and `%X` the long-time pattern. These are explicit host-culture policies,
not assertions of identical native formatting on every machine.

`FromDateTime` retains the exact supplied clock for display and the existing
midnight-based DXF day serial as its cached scalar. Binary64 cache serialization
cannot preserve every DateTime tick. `FormatJulian` uses the existing drawing-time
conversion and its range/rounding limits. A date variable requires an explicit
nonempty mask; no current-date/default-format heuristic runs.

## Angular formats

`DxfAngularValueFormat` requires `%auN` and `%prN` explicitly. Units are decimal
degrees (0), degrees/minutes/seconds (1), gradians (2), radians (3) and surveyor
bearings (4); precision is 0–8. Input is always finite radians, independent of
ANGBASE, ANGDIR, AUNITS or other drawing variables. Bearings normalize using the
existing deterministic N/S/E/W utility policy; ordinary decimal and DMS values
remain signed. Decimal output has no automatic unit suffix.

`%ds44` and `%ds46` select comma and period. Decimal `%zs0/4/8/12` selects neither,
leading, trailing or both zero suppressions. Nonzero suppression with DMS or
bearings rejects rather than silently using decimal semantics. `%ps[prefix,suffix]`
requires one comma and no nested brackets. Unknown or repeated controls reject.

DMS precision follows the existing utility: 0 gives degrees, 1–2 gives minutes,
3–4 gives seconds, and 5–8 gives one through four fractional-second places.
DMS uses a Unicode degree sign with ASCII prime marks. The exact binary64 angular
utility rounds ties to even; the separate numeric `DxfValueFormat` retains its
own ties-away policy. The radians path avoids an unnecessary radians-to-degrees-
and-back conversion. This does not add arbitrary native angular FIELD controls.

## Outcomes and atomicity

`Evaluate` returns Success or an explicit retained-cache failure. Unknown evaluator
IDs produce EvaluatorNotFound; syntax errors produce SyntaxError; unsupported
code/formats produce InvalidCode; missing explicit bindings produce InvalidContext;
arithmetic and failed dependencies produce OtherError. Provider-produced diagnostics
are bounded and flattened to a single line so runtime parameter diagnostics do
not accidentally make ASCII DXF unsavable. Explicit host-supplied failure APIs
retain their existing separate text contract.

`EvaluateOrThrow` lets errors abort the enclosing result transaction. The library
prepares every selected FIELD result before publishing, including expected failed
outcomes. No handles are allocated by this provider or result publication. Code,
evaluator metadata, original cache-format expressions, private evaluator data,
resource references, ownership and unselected roots remain unchanged. Selecting
an explicit format for computation does not rewrite the retained cache-format
metadata or certify that it matches the supplied variable's unit semantics.

Host TEXT/MTEXT/ATTRIB/TABLE displays and native checksums are not rewritten. A
native evaluator may later invalidate or recompute these cached results. The
provider is opt-in; it does not install document-event hooks or intercept native
execution. Unexpected runtime errors are not swallowed. Existing callback and
single-threaded transaction limitations remain as described in [FIELD results](field-results.md).

## Tests and qualification

The recovered baseline contains 38,328 unique passing conformance cases and
143 independent verifiers. The new C# harness adds 105 cases: five provider/
utility checks, 72 FIELD round trips and 28 unsupported-code/failure cases.
The formatting case emits 48 date and 360 angular results. It covers culture
and variable snapshots, scalar reference refusal, error policies, no-op behavior,
immutable publication and opposite-transport reloads.

The first actual compile caught stale calls to `UnitFormatMath.Finite` and an
incorrect fixed-format overload in the uploaded patch. Those calls now use the
existing checked helper signatures. All 105 cases passed in a local .NET 8 Debug
execution. The independent checker also had an obsolete helper signature; it was
corrected to use the existing Fraction-based utility oracle, with a separate
explicit surveyor quadrant calculation. Expected outputs were not weakened.

`verify_standard_field_evaluator.py` checks exactly 72 before/after FIELD pairs
and 408 date/angular results. The executed gate rejects 9,664 altered actual
outputs and audits all 144 drawings without repairs. It derives changes from
pinned FIELD packets plus explicitly synthetic evaluator code, preserving every
other ordered physical record. The established exact-empty ACAD_LAYERSTATES
identity normalization is the sole unrelated normalization. HEADER time/seed and
CLASS records remain outside that physical-record comparator.

The checker's 17 Python reference/model tests pass independently, including 911
model-record and 408 model-format corruption challenges. These checker tests are
not substituted for the separate actual library-output gate. Both missing and
extra fixture inventories reject. The full Linux/Windows Debug/Release matrix
and source/artifact pins are recorded in PR #106; local net8 execution alone
is not netstandard or legacy-framework runtime qualification.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
dotnet build netDxf/netDxf.csproj -c Release -f netstandard2.0
python tools/run_independent_verifiers.py artifacts/conformance
python -m unittest discover -s tests/standard_fields -p 'test_*.py' -v
```

Use separate DXF_TEST_ARTIFACTS directories for concurrent configurations. Normal
CI discovers the new verifier and runs the Python checker tests. No native
AutoCAD evaluator, installed-font or visual qualification is established.

## Remaining compliance work

This patch supplies bounded date/angular formatting and opt-in FIELD evaluation,
not general native expression equivalence. Arbitrary AcObjProp/object dependencies,
private evaluator controls/checksums, structural FIELD authoring, native event
policies and host-display synchronization remain outside it. Automatic TABLE
precedence, duplicated formatting, full inline/backing/private regeneration,
complete private/color schemas, recursive import, general document-version
conversion and native AutoCAD open/AUDIT/save/reopen remain unfinished. The prior
historical coverage matrix is not promoted to complete by these additions.
