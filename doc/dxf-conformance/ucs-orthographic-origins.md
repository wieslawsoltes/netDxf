# UCS orthographic origin overrides

Baseline: `b43a0d8066875b144e7f52d5e21bf43400c5e97e`, after merged PR #16.

## Implemented fields and semantics

Read, edit, clone and write the repeated UCS group 71 + 13/23/33 origin pairs. `UcsOrthographicType` maps Top=1, Bottom=2, Front=3, Back=4, Left=5 and Right=6. `UCS.OrthographicOrigins` is a stable read-only view. The validated setter, lookup and removal methods distinguish an absent override from an explicit zero point. Clones have independent override storage.

Values remain in the coordinate representation of the DXF UCS record; no extra transform is applied. In particular, the writer does not replace missing overrides with explicit zero origins. Autodesk describes the absent-pair default in terms of the existing UCS origin.

```csharp
var ucs = new UCS("Plant", new Vector3(100, 200, 300), Vector3.UnitX, Vector3.UnitY);
ucs.SetOrthographicOrigin(UcsOrthographicType.Left, new Vector3(1, 2, 3));
ucs.SetOrthographicOrigin(UcsOrthographicType.Top, Vector3.Zero);
if (ucs.TryGetOrthographicOrigin(UcsOrthographicType.Left, out Vector3 origin))
    Console.WriteLine(origin);
var copy = (UCS)ucs.Clone("Plant copy");
copy.RemoveOrthographicOrigin(UcsOrthographicType.Left); // source is unaffected
```

The reader associates coordinates with their preceding group 71, permits any XYZ order and interleaved unrelated fields, and preserves subsequent XData. It rejects invalid types, duplicate types, unpaired/duplicate coordinates and incomplete XYZ triples rather than silently overwriting or inventing points. The writer emits only explicit overrides, in canonical type order 1 through 6. Existing base origin, axes, elevation and handle ownership remain independent.

## Version coverage

| Field family | AC1015 / 2000 | AC1018 / 2004 | AC1021 / 2007 | AC1024 / 2010 | AC1027 / 2013 | AC1032 / 2018 |
|---|---|---|---|---|---|---|
| Origin pair read/write | Text + binary tested | Text + binary tested | Text + binary tested | Text + binary tested | Text + binary tested | Text + binary tested |
| Edit/delete and cross-transport save | Tested | Tested | Tested | Tested | Tested | Tested |
| Missing-pair default and malformed pairs | Tested | Tested | Tested | Tested | Tested | Tested |

These are tests of the admitted library formats, not an AutoCAD execution certificate. Pre-2000 dialects remain unsupported. Group 79, base-UCS handle 346, xref flags/dependencies and saved VIEW/VPORT UCS contexts are not implemented by this feature.

## Regression evidence

Twelve independently assembled semantic record fixtures were first run against unchanged production IO: **2,184 passed / 12 failed**, reproducing loss of all six pairs on save. They include reversed pair order, reordered components, interleaved elevation, comments and XData.

The completed feature adds **169 registered cases**: API defaults, finite-value/type validation, immutable view, explicit zero, clone isolation, all version/transport pairs, exact emitted tag order, document handles, cross-transport edits and eleven malformed-pair families. Corrected signed-library tests pass **2,353 / 0 failed** in Debug and Release. Normal SDK Linux/Windows, netstandard2.0 and source-audit checks must pass before merge.

## Primary references

- Autodesk DXF UCS group codes and origin-pair semantics: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-1906E8A7-3393-4BF9-BD27-F9AE4352FB8B.htm
- ODA Open Design DWG specification v5.4.1, section 20.4.62, printed page 177: https://www.opendesign.com/files/guestdownloads/OpenDesign_Specification_for_.dwg_files.pdf

The ODA document corroborates R2000+ UCS orthographic fields through its DWG/DXF mappings; it is not substituted for Autodesk's DXF grammar. The source-pinned 113-row matrix remains historical evidence; this feature entry supersedes its missing UCS-origin-pair status only.
