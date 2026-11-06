# FIELD and TABLE operations

Stored packets, evaluated values, layout and display blocks are separate layers.
Choose each operation explicitly; saving a document does not imply a complete
native FIELD/TABLE regeneration pass.

## FIELD evaluation

`DxfStandardFieldEvaluator` supports the exact evaluator IDs `_text`, `AcVar`
and `AcExpr`. Variables are explicitly supplied `DxfFieldVariable` values;
arithmetic and child-slot composition use bounded grammars. Variable names are
case-sensitive. Strings remain data rather than being executed as formulas.

The evaluator does not implicitly read the clock, filesystem, environment,
sheet sets, object properties or external services. Supply those values through
your application's provider. Numeric units and formatting are explicit; there
is no general cross-unit inference or universal native-expression engine.

`document.Objects.EvaluateFieldTrees` evaluates a qualified forest using the
provided callback. `evaluator.Evaluate` can persist success or an explicit failure
with retained cache; `EvaluateOrThrow` aborts the selected operation on an evaluator
error. Owned-child ordering, stale snapshots and publication are handled by the
FIELD graph APIs. Projecting results into literal text hosts is another explicit
operation, not a side effect of every evaluation.

See the [evaluator API](../../netDxf/Objects/DxfStandardFieldEvaluator.cs),
[evaluation tests](../../tests/netDxf.Conformance/StandardFieldEvaluatorTests.cs)
and [text-host tests](../../tests/netDxf.Conformance/FieldTextHostTests.cs).

## TABLE pipeline

| Stage | API and result |
|---|---|
| Address and compile | `DxfTableCellAddress` and `DxfTableFormula.Parse` provide checked addresses and bounded expressions |
| Calculate | `DxfTableCalculation.Evaluate` returns detached results from explicit formulas and a scalar resolver |
| Read stored cells | `DxfStoredTableContent.GetGrid` projects a qualified payload into an addressed immutable grid |
| Apply results | `ApplyFormulaResults` validates targets and publishes a guarded stored-content change |
| Resolve style | `DxfCellStyleResolver.Resolve` combines an explicit complete base and ordered override definitions |
| Lay out and draw | `DxfTableLayout.Create` uses supplied style/text/measurement providers; `BuildDisplayBlock` creates a detached block |
| Select display | `StoredTable.ReplaceDisplayBlock` selects a qualified registered block and clears stale proxy graphics |

Formulas use invariant finite numbers, cell references, arithmetic and the
supported aggregates `SUM`, `AVERAGE`, `COUNT`, `MIN` and `MAX`. COUNT counts
numeric results, not all addressed cells. Text is not coerced into a number or
reparsed as a formula. Cycles, unsupported functions, invalid references,
division by zero and resource-limit violations reject.

`GetGrid` associates values with their actual cell frames; unsupported content
does not shift later values into earlier cells. Formula operands require an empty
cell or one qualified scalar. Result targets must already hold an integer or
real scalar, and their stored kind is retained. Fractional or overflowing integer
results reject. Refresh the grid after committing an edit: earlier snapshots
remain immutable and can no longer be used as current edit targets.

Formula application does not also update inline TABLE values, linked geometry,
FIELD definitions and display caches. Style resolution does not infer native
row/column precedence or assign roles to ambiguous map entries: the application
supplies the ordered layers. Layout depends on the host's text measurements;
a generated display block is detached until explicitly registered/selected.

Current examples and limits are in
[calculation](../../netDxf/Tables/DxfTableCalculation.cs),
[layout](../../netDxf/Objects/DxfTableLayout.cs),
[calculation tests](../../tests/netDxf.Conformance/TableCalculationTests.cs) and
[layout tests](../../tests/netDxf.Conformance/TableLayoutTests.cs). The independent
[calculation](../../tools/verify_table_calculation.py) and
[layout](../../tools/verify_table_layout.py) checks validate exported results.
Original packet-level editing reviews are available in [history](history.md).
