# SPLINE standard periodic input

The typed reader previously required the undocumented high-bit compatibility marker 2048 and ignored the documented periodic flag (group 70, bit 2). It also rejected periodic interpretation whenever the first and last wire controls matched; that condition is normal for degree-one cyclic overlap.

Read the documented bit independently, retaining acceptance of the existing compatibility marker. Compact the degree-fold repeated wire controls and weights without changing their order or regenerating knots. Check the repeated coordinates and weights exactly before stripping them; an unsupported non-overlapping representation now fails explicitly rather than silently losing geometry. The writer continues to produce its existing canonical standard-plus-compatibility flag mask.

## Profile and representation boundaries

AC1015/2000, AC1018/2004, AC1021/2007, AC1024/2010, AC1027/2013 and AC1032/2018 are covered in text and binary. Raw profiles and historical typed admission are unchanged. Both common cyclic phases (trailing repeated initial controls and leading repeated final controls) are supported. The compact model requires at least degree+1 distinct storage slots after overlap removal. This is not support for every possible periodic control/knot representation, preservation of unknown flag bits, or complete SPLINE structural validation. Non-overlapping periodic metadata is rejected, not guessed or silently downgraded. Default/missing weights keep the existing unit-weight interpretation; general malformed weight/count handling is separate.

The public control polygon is compact, whereas the written DXF controls contain cyclic overlap. The raw output retains each supplied coordinate and knot as the same binary64 value. A default-weight input normalizes to explicit unit weights on export. Public `IsClosed` remains control-polygon endpoint equality; `IsClosedPeriodic` identifies periodic closure. No API or signing/target-framework change is made.

## Executed validation

Baseline: merged PR65, commit `7c0dca2c512b9774ff513b5b1cfa5f03021e4de5`, tree `0a277261d89723294c0c67aa21256530291ee97f`.

528 registered cases cover six profiles, both transports, degrees 1/2/3, standard-only/legacy/combined flags, optional rational weights, both cyclic phases, exact output coordinates/knots, nested clones, three alternating transport cycles, following XData/entities, and refusal of non-matching cyclic controls/weights.

The exact final tests on unchanged production: **14,839 pass / 432 fail**, Debug and Release. Corrected signed-library .NET 8 execution: **15,271 pass / zero fail**, both configurations. Initial development fixtures lacked `$DWGCODEPAGE`; it was added before both final red/green runs. No code-page checks were weakened. The 15 Python documentation-integrity tests pass separately.

The independent `verify_spline_periodic_input.py` checks 36 files / 72 periodic splines with ezdxf 1.4.4, including exact wire control/weight/knot arrays, standard flags, adjacent XData and following LINE. Zero audit errors and zero repairs. Native AutoCAD has not been executed.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_periodic_input.py artifacts/conformance
```

Primary reference: [Autodesk SPLINE DXF group codes](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm), specifically documented bit 2. The cyclic compact representation is this library's API contract, not an extra requirement claimed to appear in that table. The main comparison is refreshed after implementation merges.
