# DXF conformance audit and implementation ledger

Audit baseline: `5b562312f683fc635405c149537ca488e4ec4d39` on `netstandard`.
Audit started: 2026-09-12. This document is a coverage inventory, not a certification of full AutoCAD compatibility.

## Acceptance contract

A DXF version is not a single feature. Track transport, section grammar, record/group-code coverage, object ownership, semantic editing, preservation and downgrade behavior independently. An enum member or a successful LINE round trip is not evidence that an entire version is implemented. Keeping opaque records is preservation, not semantic editing or geometric evaluation.

Every implementation PR must cite its applicable specification, add positive and malformed-input regression tests, exercise every applicable file version and text/binary transport, state any intentional downgrade, and pass CI before merge. Merge one feature before starting the next feature unless their changes are independent. Do not silently omit unsupported entities or their dependencies.

## Version baseline

The mapping below is the mapping declared in `netDxf/Header/DxfVersion.cs`; historical aliases still require independent fixtures. Actual document loading is limited to AutoCAD 2000 and later. Product release years must not be invented as distinct DXF database formats.

| Version | Declared `$ACADVER` | Document read/write baseline | Required remaining verification |
|---|---|---|---|
| R1.1 | MC0.0 | Not supported | Historical grammar and authoritative fixtures |
| R1.2 | AC1.2 | Not supported | Historical grammar and authoritative fixtures |
| R1.4 | AC1.4 | Not supported | Verify historical spelling/aliases and fixtures |
| R2.0 | AC1.50 | Not supported | Historical grammar and fixtures |
| R2.10 | AC2.10 | Not supported | Historical grammar and fixtures |
| R2.5 | AC1002 | Not supported | Historical grammar and fixtures |
| R2.6 | AC1003 | Not supported | Historical grammar and fixtures |
| R9 | AC1004 | Not supported | Legacy section/entity grammar |
| R10 | AC1006 | Not supported | Legacy text/binary transport and entities |
| R11/R12 | AC1009 | Not supported | Legacy binary group-code width; old entity/table grammar |
| R13 | AC1012 | Not supported | Release-specific section, handle and subclass behavior |
| R14 | AC1014 | Not supported | Release-specific entities, objects and writer |
| 2000 | AC1015 | Text and binary advertised | Per-record and group-code conformance; lossless preservation |
| 2004 | AC1018 | Text and binary advertised | Above plus version-specific color/hatch/data differences |
| 2007 | AC1021 | Text and binary advertised | Above plus encoding and newer record families |
| 2010 | AC1024 | Text and binary advertised | Above plus mesh/annotation/association differences |
| 2013 | AC1027 | Text and binary advertised | Above plus auxiliary/opaque payload preservation |
| 2018 | AC1032 | Text and binary advertised | Complete current-family record and group-code verification |

The initial tests cover one LINE and one CIRCLE in each of the twelve advertised version/transport combinations. They do not establish full feature conformance or AutoCAD interoperability.

## Entity coverage declared by the repository

The README advertises 3DFACE, ARC, CIRCLE, eight dimension variants, ELLIPSE, HATCH including gradients, IMAGE, INSERT/attributes, LEADER, LINE, LWPOLYLINE, MESH, MLINE, MTEXT, POINT, legacy POLYLINE variants, RAY, SHAPE, SOLID, SPLINE, TEXT, TOLERANCE, TRACE, DGN/DWF/PDF UNDERLAY, WIPEOUT and XLINE. These are implementation candidates to audit at group-code level, not a claim that every property exists in every target version.

Known declared limitations: dynamic blocks are unsupported; TABLE imports are converted to INSERTs; REGION, SURFACE and 3DSOLID are excluded. A record may be preservable without evaluating its proprietary geometry. This distinction replaces any assumption that unsupported payloads must be discarded.

The detailed source-driven feature matrix and dependency inventory will be expanded as the reader/writer dispatch and version gates are audited.

## Work ordering

1. Baseline version tests and CI in both Debug and Release on Linux and Windows; build existing library targets without dropping compatibility.
2. Correct transport defects with failing-then-passing tests: complete binary sentinel validation, truncated binary chunks, malformed text values, and type-range consistency.
3. Central version/feature capability and explicit downgrade diagnostics.
4. Preservation of ordered tags, sections, classes, unknown entities/objects, subclass data, extension dictionaries, reactors and handle references.
5. Legacy R12/R13/R14 reader/writer dialects, then earlier releases backed by fixtures.
6. Missing standard entities and objects, one semantic family per PR, followed by currently unmodeled properties of supported families.
7. ACIS/ASM and application payload preservation versus semantic geometry integration; dynamic/annotative/associative dependency graphs.
8. Independent CAD interoperability, fuzzing, quotas, streaming and performance gates.

## Regression findings queued from source inspection

- `BinaryCodeValueReader` checks only the first eighteen bytes of the 22-byte sentinel and indexes a potentially short `ReadBytes` result.
- Binary chunks use `ReadBytes(length)` without checking that the declared length was read.
- `DxfDocument` has different exception behavior in Debug and Release; both builds must be tested. File overloads also need exception-path resource audits.

These are findings, not completed fixes. Each fix must have its own verified PR entry.

## Test commands

```sh
dotnet build netDxf/netDxf.csproj -c Release
dotnet run --project tests/netDxf.Tests/netDxf.Tests.csproj -c Debug
dotnet run --project tests/netDxf.Tests/netDxf.Tests.csproj -c Release
```

## Primary references

- Repository README, `netDxf/Header/DxfVersion.cs`, `netDxf/DxfDocument.cs`, `netDxf/IO/BinaryCodeValueReader.cs` at the baseline above.
- Autodesk DXF reference contents: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/index.htm
- Autodesk DXF format definition: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-235B22E0-A567-4CF6-92D3-38A2306D73F3.htm

Newer product releases require verification of their actual `$ACADVER` and release-specific records; no separate 2021/2024/2027 file format is asserted here.
