# Exact dimension tolerance bounds and sparse override inheritance

The typed writer now emits the stored lower dimension tolerance in `$DIMTM`
without replacing zero or a small number by `MathHelper.Epsilon`. The DIMSTYLE
47/48 and DSTYLE 47/48 values already retained the original values; the header
now agrees. Positive and negative zero, subnormals, small positive/negative
values, and one-ULP differences are not used as display-mode sentinels.

The DIMSTYLE and DSTYLE readers infer symmetrical versus deviation presentation
from **exact numeric equality**, not approximate geometric equality. When DSTYLE
selects tolerance presentation but omits either bound, inference uses that bound
from the resolved base style. Missing overrides remain missing: reading does not
materialize inherited numeric values. Dimensions and LEADER in model space,
paper space and referenced/unreferenced blocks share the corrected path.

## Representation and compatibility

DXF encodes tolerance display using DIMTOL/DIMLIM flags plus DIMTP/DIMTM bounds.
Both the library's `Symmetrical` and `Deviation` enum choices emit flags 1/0.
Consequently, equal bounds decode as `Symmetrical`, even when an author explicitly
selected `Deviation`; no fabricated epsilon is written to distinguish two enum
labels that have identical physical flags and equal values. Signed zeros compare
equal for mode inference, while the original sign bits of the bound values are
retained. None and Limits modes and their flag precedence are unchanged.

This change does not define new behavior for incomplete/malformed flag pairs,
nonfinite tolerance values or concurrent source mutation. No new historical typed
profile or arbitrary private-schema interpretation is enabled. Direct raw snapshot
editing, dimension geometry/label generation, public signatures, and unrelated
style/override settings remain unchanged. It does not implement tolerance labels;
that remains a separate formatter task.

```csharp
var style = new DimensionStyle("INSPECTION");
style.Tolerances.DisplayMethod = DimensionStyleTolerancesDisplayMethod.Deviation;
style.Tolerances.UpperLimit = 1e-13;
style.Tolerances.LowerLimit = 0.0; // Written as zero, not as the global epsilon.
```

## Qualification

The focused tests use all six existing typed profiles and both transports, exact
header/table comparisons, sparse method/one-bound/two-bound overrides, independent
clone and source-XData checks, all four ownership placements, dimension/LEADER,
and source/resave/reload cycles. The independent reader checks exact physical
header/table/XData values, flags, absent fields, opaque neighbors, ownership and
object graphs. Deliberately altered, omitted and duplicate packets and missing or
extra drawing inventories must fail. Fixtures are synthetic, not native producer
evidence. The PR records actual executions; planned cases are not execution proof.

```sh
DXF_TEST_FILTER=tolerance-fidelity/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_tolerance_fidelity.py artifacts/conformance
```

Autodesk's [DIMSTYLE group-code reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm)
identifies DIMTP (47), DIMTM (48), DIMTOL (71) and DIMLIM (72). Exact mode inference
is this library's representation rule, not an assertion about every native API.
Native AutoCAD open/AUDIT/save/reopen, font/fit/tolerance layout equivalence,
historical typed dialects/pre-R11, private FIELD/TABLE/cache regeneration,
dependency-complete imports and general version conversion remain unqualified.
