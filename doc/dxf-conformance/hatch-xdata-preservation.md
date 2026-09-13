# HATCH origin XData without destructive source mutation

Baseline: `d37954abcf54b8b2eb3e32ab050504e6350a9ead`, after merged PR #43. Audit date: 13 September 2026.

## Corrected data loss

The writer cleared the complete ACAD XData list before inserting the pattern origin. For gradient fills it also cleared both GradientColor1ACI/GradientColor2ACI lists. This discarded unrelated data in the saved drawing and modified the caller's in-memory entity during Save. The reader independently assigned every encountered ACAD 1010/1020 component to the origin, including nested and later unrelated points.

The reader now selects the first unbraced, consecutive 1010/1020/1030 tuple. Nested points and incomplete tuples do not supply an origin. The writer constructs temporary record-list projections, replaces only that tuple, or prepends it when absent. Later tuples, braces, strings, integers, opaque binary values and their order are retained. The managed origin Z is normalized to zero as before. This is a documented compatibility convention for netDxf's existing hatch-origin representation, **not a complete Autodesk ACAD application-data schema**. Ambiguous application-specific unbraced points still need an application schema; arbitrary data should not be authored under the reserved ACAD APPID.

Gradient color projections replace only a leading Int16, or prepend one when absent. They retain suffix records. APPID comparison is case-insensitive; existing order/case and unrelated applications are preserved. Missing managed applications are generated in the output without being attached to the source entity. The existing writer registers their APPIDs before emitting TABLES. The primitive XData serializer, character encoding, chunk policy and other entities' behaviour remain unchanged.

No temporary XDataDictionary is used, avoiding subscriptions to source-object events. Record lists and stored binary arrays are read but never mutated by this projection. Tests assert the source application count, record values, record-object identities and byte contents after successful and injected-failure saves. This does not make the whole DxfDocument.Save operation immutable or transactional: other existing serializer bookkeeping, partial destination writes and concurrent mutation policies are unchanged.

## Version and API behaviour

| Operation | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Origin reading with nested/suffix XData | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary |
| Non-destructive output projections | Tested | Tested | Tested | Tested | Tested | Tested |
| Repeated alternating-transport saves | Tested | Tested | Tested | Tested | Tested | Tested |
| Failed-output source preservation | Tested | Tested | Tested | Tested | Tested | Tested |

Pattern, solid and gradient fills are covered. The existing AC1015 gradient-to-solid conversion is retained, including its managed gradient metadata. No historical typed profile is added. Public Save retains its Debug exception / Release false contract.

Existing callers continue setting `hatch.Pattern.Origin`. Save reflects that value in the output without modifying `hatch.XData`. Code that relied on Save populating/replacing the source XData must instead inspect the saved document or update its own model explicitly. Source XData and the typed origin can differ until serialized; output reconciliation is deliberate and confined to the identified fields.

## Executed regression evidence

156 additional registered cases. The **final** tests against unchanged PR #43 production report **6,452 passed / 144 failed** in both Debug and Release. The corrected signed production library reports **6,596 passed / 0 failed** in both configurations on the local .NET 8 workbench. An initial incomplete-tuple test accidentally supplied an Int32 for RealX; that fixture was corrected to a double before these final old/fixed comparisons. No production validation or assertion was weakened.

Coverage includes first/last/nested/missing/incomplete/multiple origin tuples, unchanged opaque data, three fill types, three alternating save/load cycles, absent applications, case-insensitive APPIDs, gradient prefixes/suffixes, source record identity, stream ownership and injected late-output failures. Existing 6,440 cases continue passing. Context preservation is not a claim that every synthetic metadata combination is a valid Autodesk schema.

The optional **ezdxf 1.4.4** verifier checks all twelve retained output files, including ordered origin/nested/binary/suffix values, managed gradient indices, an unrelated APPID, seeds, elevation and boundaries. All pass with **zero audit errors and zero repairs**, checked only after inspecting the unmodified values. No native AutoCAD process was executed.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_xdata_preservation.py artifacts/conformance
```

Final-head Linux/Windows Debug/Release, netstandard2.0 and documentation/source checks remain merge gates. Full ACAD schema interpretation, strict general XData structure/size validation, coordinate-reference evaluation, dependency remapping and complete HATCH transformation fidelity remain separate work.

## Primary sources

- Autodesk HATCH example carrying ACAD point data and protection of unrelated application data: https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-AutoLISP/files/GUID-1A2A0518-E9DB-462E-925E-32181D96CE4D.htm
- Autodesk XData order, per-application grouping, nested control lists and primitive value semantics: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A2A628B0-3699-4740-A215-C560E7242F63.htm
