# HATCH outer boundary-count validation

## Defect and correction

The outer group-91 reader previously searched for group 92 or 93 until it accumulated the advertised number of paths. It skipped arbitrary scalar data, new entity markers and section boundaries during that search. Negative counts silently produced an empty boundary, duplicate group 91 replaced earlier paths, and surplus paths could be discarded by the enclosing HATCH parser. Edge paths could inherit flags from an earlier path when group 92 was missing.

The reader now treats this data as a counted list of complete packets. It requires a nonnegative group-91 count, a group-92 flag for each path, and group 93 after non-polyline flags. Existing polyline/edge parsers consume each packet and its source-reference list. No collection capacity derives from an untrusted path count. Duplicate group 91 and group 92/93 outside the declared list are rejected with group-code/position context. A wrong code, including an entity or section marker, terminates parsing with an error rather than causing a search through later records.

Comments in text DXF are skipped between packet fields. Entire boundary packets can still occur before or after scalar/pattern/XData content; this is not permission to interleave unrelated records inside a counted packet. The field-specific inner readers, flag preservation, UTF/code-page handling, public Debug exceptions / Release null returns, and caller-owned stream lifetime remain unchanged.

## Version comparison

| Profile | Typed behavior after this correction | Separate raw pipeline |
|---|---|---|
| R11/R12 AC1009, R13 AC1012, R14 AC1014 | Still not admitted | No raw behavior changes |
| 2000 AC1015 | Checked outer framing, text and binary | Unchanged |
| 2004 AC1018 | Same | Unchanged |
| 2007 AC1021 | Same | Unchanged |
| 2010 AC1024 | Same, including existing spline-local fit packets | Unchanged |
| 2013 AC1027 | Same | Unchanged |
| 2018 AC1032 | Same | Unchanged |

Zero or absent boundary counts **with no boundary payload** retain the existing typed policy: the empty HATCH is discarded, while following entities remain readable. This is an explicit compatibility behavior, not lossless empty-HATCH support. A zero count followed by undeclared path data is now rejected. Unknown path flag bits continue to be preserved; this change does not validate geometry or redefine flags.

## Executed regression evidence

Baseline: merged PR #57, commit `0f932121f911817aae1220ee95a8944f06f6ffba`, tree `739f3d7cffec64b26f5a3a83964b5a8b29d8fc5f`.

`HatchPathCountTests.cs` adds 408 independently encoded registered cases across all six typed profiles and both transports. Cases cover zero/one/two/four paths, mixed closed polyline/circular edges, per-path flags, late whole packets, comments, negative/undersized/oversized/duplicate/absent counts, missing headers, orphan fields, intervening entity/section markers, `int.MaxValue`, physical EOF, repeated transport changes and following LINE preservation.

The identical tests on unchanged production report **12,715 passed / 192 failed in Debug** and **12,751 passed / 156 failed in Release**. The difference comes from precise Debug exception diagnostics versus the existing Release null-return convention. Corrected signed production passes **12,907 / zero failures** in both local .NET 8 configurations. No baseline failures occur in the valid/empty/EOF compatibility cases.

The independent ezdxf 1.4.4 verifier checks twelve exported files / 24 boundary paths, exact flags, square vertices, full-circle parameters, adjacent metadata and the following LINE, with **zero audit errors and zero repairs**. The 15 documentation-integrity tests are separate from the C# count. Final-head Linux/Windows Debug/Release, netstandard2.0 and source-audit CI must pass before merge.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_path_count.py artifacts/conformance
```

## Primary source and remaining limits

Autodesk [AutoCAD 2012 DXF Reference](https://images.autodesk.com/adsk/files/autocad_2012_pdf_dxf-reference_enu.pdf), printed pages 88 and 90–91 (PDF pages 96 and 98–99), defines the outer count and individual boundary packets. The tables were inspected directly.

This correction is lexical/count validation, not closure/intersection/containment checking, association repair, source-handle dependency validation, resource quotas for an arbitrarily large genuinely populated file, whole-HATCH schema validation or native AutoCAD qualification. No full-standard completion percentage is inferred from the test count.
