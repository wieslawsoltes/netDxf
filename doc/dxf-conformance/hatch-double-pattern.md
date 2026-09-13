# HATCH double-pattern flag

Baseline: `f1bb3cfa48131531b4e814b60192a00146ae7eb2`, after merged PR #38. Audit date: 13 September 2026.

## Defect and delivered contract

The reader ignored group 77 and the writer always emitted zero. A doubled user-defined hatch therefore lost the setting during ordinary load/save and cloning. `HatchPattern.IsDouble` now exposes the Boolean field, defaults to false, reads canonical 0/1, and writes the current value. Other values fail with a group-77 diagnostic instead of being silently discarded. The public Debug-exception / Release-null behavior is unchanged.

For a user-defined pattern, the flag requests a second set of lines perpendicular to the original set. It does not insert or rotate extra `LineDefinitions` in the model. Existing flags on predefined/custom pattern fills are retained, although AutoCAD ignores their double setting. `HatchPattern.Clone`, `HatchGradientPattern.Clone`, `Hatch.Clone`, nested INSERT cloning and INSERT explosion preserve independently editable state. Existing HATCH transformations retain the flag.

The field applies only to pattern fills. Solid and gradient output continues to omit group 77; an inapplicable flag can remain dormant in the in-memory pattern without mutating the source during Save. An absent input flag becomes false and canonical output emits zero. This is semantic field fidelity, not optional-tag or lexical identity. The separate raw API remains the preservation route for exact input data.

```csharp
using netDxf;
using netDxf.Entities;
using netDxf.Header;

var pattern = HatchPattern.Line;
pattern.Type = HatchType.UserDefined;
pattern.IsDouble = true;
var boundary = new HatchBoundaryPath(new[] { new Circle(Vector3.Zero, 10.0) });
var hatch = new Hatch(pattern, new[] { boundary }, associative: false);
var copy = (Hatch)hatch.Clone();
copy.Pattern.IsDouble = false; // source remains doubled
var drawing = new DxfDocument(DxfVersion.AutoCad2018);
drawing.Entities.Add(hatch);
drawing.Save("double-hatch.dxf");
```

No boundary generation, fill evaluation, renderer, pixel-size behavior, seed-point support, PAT-file double-flag persistence or general HATCH-schema validation is added.

## Version comparison

| Operation | AC1015 / 2000 | AC1018 / 2004 | AC1021 / 2007 | AC1024 / 2010 | AC1027 / 2013 | AC1032 / 2018 |
|---|---|---|---|---|---|---|
| Read/write enabled, disabled and absent flag | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary |
| Reject non-Boolean values | Tested | Tested | Tested | Tested | Tested | Tested |
| Clone, INSERT explosion, edit/reload | Tested | Tested | Tested | Tested | Tested | Tested |
| Retain authored line definitions and metadata | Tested | Tested | Tested | Tested | Tested | Tested |
| Solid/gradient field omission | Tested | Tested | Tested | Tested | Tested | Tested |

The existing AC1015 gradient downgrade behavior is unchanged; omission tests do not assert gradient support there. R12/R13/R14 typed admission is also unchanged. These are supported implementation profiles, not a claim that the field originated in 2000.

## Executed evidence

180 independent-byte cases against unchanged PR #38 production: **5,956 passed / 108 failed**, both Debug and Release. Thirty-six enabled-flag combinations lost their setting, and 72 invalid-value combinations were accepted. A fixture originally omitted DWGCODEPAGE and triggered the existing Debug assertion; the final red/green comparison uses explicit ANSI_1252 in all fixtures rather than changing that production policy.

The fixed code passes the same 180 cases. Another 37 API/default/clone/late-order/solid-fill cases produce the final total: **6,101 passed / 0 failed**, both locally compiled signed .NET 8 configurations. The netstandard2.0 target and Linux/Windows SDK CI remain required merge gates. Older target compilation does not establish older-runtime execution.

The included independent ezdxf 1.4.4 verifier loads all twelve retained original-plus-clone drawings, checks the flag, authored line count/spacing, elevation, boundary count and XData, and reports **zero audit errors / zero repairs**. This is independent fixture agreement, not AutoCAD execution or visual fill certification.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_double.py artifacts/conformance
```

## Primary references

- Autodesk HATCH group 77 (pattern-fill Boolean flag): https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm
- Autodesk user-defined hatch semantics: https://help.autodesk.com/cloudhelp/2019/ENU/OARX-RefGuide/files/OREF-AcDbHatch__HatchPatternType.html
- Autodesk HatchPatternDouble behavior for predefined/custom patterns: https://help.autodesk.com/cloudhelp/2026/DEU/AutoCAD-ActiveX-Reference/files/GUID-645969D5-9475-4413-8AD1-8F410DBD333B.htm
