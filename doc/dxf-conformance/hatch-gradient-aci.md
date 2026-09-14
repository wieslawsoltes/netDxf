# Optional HATCH gradient ACI identity and presence

## Implementation checkpoint

Recovered the previously unmerged ACI increment and reconciled it with merged PR #59, commit `77e7cdbd86fa0c8d2d31bf1d0a1eaaab1e006767`, tree `a4168bbd3663e539e264c594d80bebd6903bed56`. Both the newer spline-fit and outer boundary-count changes remain intact. Only this ACI field-preservation feature, its tests and this feature note enter the PR; the source-pinned aggregate ledger is refreshed separately after merge.

## Defect and three-state API

PR #55 accepts optional group-63 ACI fields independently for each gradient color stop, but discards their values. The writer always emits `Color1.Index` and `Color2.Index`. Consequently, load/save can change an authored ACI index and can add a tag where no tag was present. Both stored RGB colors may remain correct while this metadata is lost.

The model now exposes `short? Color1AciIndex` and `short? Color2AciIndex`, with independent modes:

| State | API behavior | Output |
|---|---|---|
| Automatic | Default for a newly constructed pattern; getter follows the current RGB color's `Index` | Same derived group-63 output as before this change |
| Explicit index | Assign a nullable property a value, or read a present group-63 field | Emit that exact Int16 value for the corresponding stop |
| Explicit absence | Assign null, or read a stop without group 63 | Omit that stop's group-63 field |

`IsColor1AciIndexAutomatic` and `IsColor2AciIndexAutomatic` expose the modes without changing them. `ResetColor1AciIndex()` and `ResetColor2AciIndex()` restore automatic derivation. A null property assignment deliberately means absence, not automatic mode.

The reader retains each stop's nullable index alongside its separate RGB color. The writer emits only present values and does not mutate the pattern. Clone copies both the backing value and automatic-mode flag: cloning an automatic pattern must not freeze a derived value, and cloning an imported absent field must not synthesize one. Existing direct/nested HATCH clone and INSERT explosion use this path.

Explicit ACI metadata is independent of RGB replacement, mutable RGB/index edits, tint changes and single-color dialog mode. Assigning an ACI property does not recolor a stop or select two-color mode. Clients that want an edited RGB color to select a new fallback ACI can explicitly reset its automatic mode. Imported RGB remains independently authoritative; an ACI-only stop without required RGB is not a new schema.

The property retains the complete Int16 value without adding palette-range validation or interpreting special values. The signed-endpoint regression probes are **numeric preservation tests**, not a claim that every Int16 is a legal AutoCAD palette index. This avoids converting the former permissive read of that metadata into an unrelated new rejection policy.

```csharp
var pattern = new netDxf.Entities.HatchGradientPattern
{
    Color1AciIndex = 17,   // Explicit metadata; does not replace the RGB stop.
    Color2AciIndex = null // Explicitly omit the second ACI tag.
};
var copy = (netDxf.Entities.HatchGradientPattern)pattern.Clone();
copy.ResetColor2AciIndex(); // Resume the previous computed-fallback behavior.
// The original still has an absent second ACI tag.
```

## Version comparison

| Family | Typed input with this change | Typed output |
|---|---|---|
| R11/R12 / AC1009 | Still rejected by DxfDocument | Separate raw preservation remains unchanged |
| R13 / AC1012 | Still rejected | Same |
| R14 / AC1014 | Still rejected | Same |
| 2000 / AC1015 | Existing permissive gradient import preserves optional metadata in memory | Existing lossy solid-fill downgrade still removes the gradient packet |
| 2004 / AC1018 | Preserve each group's value or absence without changing either RGB stop | Text/binary; exact stop-specific Int16 and presence |
| 2007 / AC1021 | Same | Same |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

No historical version admission, writer downgrade policy, public target framework or strong-name identity was changed. Existing new-pattern ACI output is retained by the default automatic mode. A serialized automatic value becomes explicit metadata when read again; the automatic policy itself is an authoring state, not a DXF field.

## Executed red/green evidence

`HatchGradientAciTests.cs` adds **934 wire-behavior cases**: all six typed families, text/binary, all nine gradient names, both color modes, all four per-stop presence combinations, reversed ACI/RGB component order, and seven signed Int16 probes for each gradient-capable version/transport. The source bytes are encoded independently of the production writer. Each supported round trip checks exact ordered ACI presence/value, both RGB colors, dialog state, angle/shift/tint, nested clone/INSERT explosion, three alternating transports, boundaries/seeds/elevation, XData and a following LINE. AC1015 cases explicitly retain its existing downgrade.

With those identical wire tests, unchanged PR #59 reports **13,051 passed / 790 failed in both Debug and Release**. Those are behavioral loss failures, not compiler errors. All of those cases pass with the correction. The earlier unmerged PR #56-based comparison reproduced the same 790 ACI failures; the current comparison retains all 744 merged spline-fit and boundary-count cases.

`HatchGradientAciApiTests.cs` adds **33 further direct API cases**, covering six constructors, all nine automatic/value/absent clone-state combinations, each imported presence combination, automatic output without source mutation, exact Int16 assignment, reset, mutable color edits and independence from tint/color mode. Final local signed-library results are **13,874 passed / zero failed in Debug and Release**: 12,907 baseline cases plus 967 new cases. The wire-only red comparison deliberately precedes API tests that cannot compile against an assembly without the new public API; the counts are not presented as identical final-suite red/green totals.

Local builds used the .NET 8.0.425 Roslyn compiler, .NET 8 reference assemblies and runtime 8.0.31, compiling the production library as a separate strong-named assembly with the repository key. This is actual execution but not a new SDK/MSBuild or Windows CI run. Final-head Linux/Windows Debug/Release SDK checks, netstandard2.0 builds and source/ledger checks remain merge gates. Baseline CI [34818022288](https://github.com/wieslawsoltes/netDxf/actions/runs/34818022288) validates PR #59; final-head validation is required for this increment.

## Independent verification and a reader caveat

`verify_hatch_gradient_aci.py` checks forty exported files: five gradient-capable profiles, two transports and all four ACI-presence combinations, with two gradients per file. The ezdxf 1.4.4 ASCII/binary tag decoders independently verify the exact **eighty stop-specific ACI pairs**, the two RGB integers and the group-463 stop boundaries. Its object model verifies adjacent gradient/drawing state and its audit reports **zero errors and zero repairs**.

The same verification exposed an important oracle limitation: ezdxf 1.4.4 `Gradient.load_tags` assigns ACI values by occurrence rather than by group-463 stop. When only the second index is present, its object model reports that value as `aci1`. The verifier explicitly reports this known discrepancy for twenty of the eighty gradients and checks their true stop association with the independently decoded ordered tags. It does not silently reinterpret the object model as correct or claim semantic ACI agreement for those twenty records. The upstream exporter itself supports independent omission of each ACI tag.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_gradient_aci.py artifacts/conformance
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
```

## Remaining scope

Palette validation, native AutoCAD interpretation of unusual indices, byte-identical packed-RGB high bytes, complete solid-tail preservation, future gradient names/reserved meanings, extreme-angle normalization, general affine gradient evaluation and centralized loss diagnostics remain separate. No AutoCAD process was executed, and no full-standard or rendered appearance qualification is claimed. The raw document API remains a separate pipeline, not an implicit typed fallback.

## Primary sources

- [Autodesk HATCH](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm): gradient fields and ordered stop controls. This public table does not explicitly describe optional per-gradient ACI omission; that behavior is not falsely attributed to it.
- [ezdxf 1.4.4 gradient source](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/gradient.py): nullable `aci1`/`aci2`, conditional group-63 output, and the occurrence-based import caveat described above.
- [Autodesk entity-code guidance](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm): scalar table order is not a parser grammar.
