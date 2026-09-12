# DXF conformance audit and implementation ledger

Audit started: 2026-09-12. Baseline: `5b562312f683fc635405c149537ca488e4ec4d39`, branch `netstandard`.

## Acceptance rule

A recognized `$ACADVER`, an entity class, successful load, or a self-round-trip is not proof of full DXF compliance. Track syntax, semantic reading, semantic writing, unknown-data preservation, downgrade policy, and external AutoCAD validation separately. No version is certified complete by this initial test harness.

## Version baseline

| Database format | Header | Baseline read/write | Principal audit boundary |
|---|---|---|---|
| AutoCAD 2000 | AC1015 | Implemented, text and binary | Oldest admitted semantic document version; field-level completeness unverified |
| AutoCAD 2004 | AC1018 | Implemented, text and binary | Audit true color, gradients, version-specific fields, and downgrade behavior |
| AutoCAD 2007 | AC1021 | Implemented, text and binary | Audit UTF-8 and newer annotations, geometry, and object families |
| AutoCAD 2010 | AC1024 | Implemented, text and binary | Audit subdivision meshes, underlays, and newer entity/object fields |
| AutoCAD 2013 | AC1027 | Implemented, text and binary | Audit ACDSDATA and modeler payload preservation |
| AutoCAD 2018 | AC1032 | Implemented, text and binary | Audit maintenance-version changes and current AutoCAD-generated fixtures |
| R11/R12 | AC1009 | Enum/detection only; semantic load/save unsupported | Requires a genuine legacy codec/profile, not merely removing the version guard |
| R13 | AC1012 | Enum/detection only; semantic load/save unsupported | Requires independent historical fixtures and binary framing verification |
| R14 | AC1014 | Enum/detection only; semantic load/save unsupported | Requires legacy profile and field/version constraints |
| Earlier recognized formats | MC0.0 through AC1006 | Enum values are not document support | Historical specification and fixture audit required |

## Known baseline limitations

The baseline README explicitly excludes dynamic blocks and imports AutoCAD TABLE entities as INSERTs. REGION, BODY/3DSOLID, and SURFACE need separate consideration of their documented DXF envelope, opaque modeler payload preservation, and actual geometry semantics. Undocumented payloads must not be silently dropped or falsely advertised as editable.

The entity enum and README expose established drawing primitives, dimensions, hatches, images, inserts/attributes, leaders, polylines and meshes, text, rays, shapes, solids/traces, splines, tolerances, underlays, viewports, wipeouts, and construction lines. Their existence is implementation evidence only; per-group-code coverage remains to be measured.

## Reproduced source defects awaiting isolated fix PRs

- Binary sentinel validation compares only the first 18 bytes, not the full 22-byte signature. Short inputs can cause an incidental indexing exception; invalid sentinel suffixes are accepted.
- Binary chunk parsing returns `BinaryReader.ReadBytes(length)` without verifying the requested length. A truncated chunk can be returned as if complete.

## Test strategy

`dotnet run --project tests/netDxf.Conformance -c Debug` and the corresponding Release run execute the actual signed library. The initial fixtures cover all six supported version families in text and binary, checking LINE, CIRCLE, ARC, POINT, version detection, and ownership of caller-provided streams. Low-level codec tests use reflection against the production assembly, not a duplicate source compilation.

CI runs on Linux and Windows in Debug and Release, builds the netstandard2.0 API target, and retains generated DXFs, JSON test results, a static inventory, and an exact source archive. Static inventory is deliberately labeled as evidence rather than proof of standards coverage.

Every implementation PR must add regression tests, update this ledger or the detailed matrix, pass the applicable checks, and be merged before the next dependent feature starts. Preserve public API, target frameworks, signing, and existing behavior unless an explicit bug fix requires a documented change.

## Definition of completion

Completion requires a source-linked feature/field matrix for each target format; deterministic diagnostics for invalid input; explicit reject/convert/preserve behavior for down-saving; preservation of unknown entities, objects, extension dictionaries, reactors, classes, and opaque payloads; and an independent fixture corpus opened and audited in AutoCAD. Merely expanding the enum or generating new files is insufficient.

## Primary references

- [Autodesk DXF reference index](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/index.htm)
- [Group-code value types](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-2553CF98-44F6-4828-82DD-FE3BC7448113.htm)
- [Binary DXF framing](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm)
- [Header group codes](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm)
- [Current Autodesk format-family compatibility](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/AutoCAD-drawing-file-format.html)

The historical 2017 DXF index is a documented baseline, not an assertion that it enumerates every newer or application-specific class. Current references and independent historical fixtures must qualify individual version claims.
