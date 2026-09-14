# Typed LIGHT entities

## Implemented contract

`Light : EntityObject` represents the published `AcDbLight` fields: schema version, name, distant/point/spot type, status, plot glyph, intensity, WCS source and target positions, attenuation type and limits, hotspot/falloff angles, shadow enable/type/map size/softness. Common entity display color and visibility remain distinct from light status and intensity. `DrawingEntities.Lights` enumerates the active layout; registration/removal, block persistence, deep clones and uniform-scale INSERT explosion are integrated.

All published scalar fields are read by group code, including fully reversed order and separated coordinate components. Supplied vectors must have all three components; absent vectors use documented API defaults. Duplicate recognized fields, invalid enums, negative/nonfinite constrained values, incomplete vectors and unexpected subclass/private-control markers reject rather than changing another field's meaning. XData and following entities remain intact. Output is canonical, not a byte-exact reproduction of omitted defaults or original field ordering. Unknown scalar extensions have the existing typed-pipeline limitation; use the separate raw pipeline for opaque preservation.

The API uses degrees without angle normalization and drawing-unit attenuation distances. Parameters may remain dormant for another light type. It does not silently enforce a hotspot/falloff relation or regenerate attenuation limits while editing another field. Names support Unicode; null is rejected. Numerical setters validate before assignment. This is stored lighting data, not a photometric evaluator, renderer, SUN implementation, LIGHTLIST object or private web/physical-light schema.

## Versions

| Format | Typed import | Typed export |
|---|---|---|
| R11/R12, R13, R14 | Typed document remains rejected | Separate raw preservation only |
| 2000 / AC1015 | Permissive import retains LIGHT fields | Reject before preprocessing and caller-stream writes |
| 2004 / AC1018 | Same | Same |
| 2007 / AC1021 | Published parameter set | Text/binary |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

2007+ is the conservative writer profile, also used by ezdxf, not proof of every field's first historical occurrence. Preflight inspects model/paper/nested/unreferenced registered blocks. Original file-path `Save` is still nontransactional; `SaveAtomic` is available explicitly. Native AutoCAD has not been executed.

## Transform and clone semantics

Finite nonsingular similarity transforms, including reflection, transform source and target as points and scale attenuation distances by the common magnitude. Intensity, cone angles and shadow-map settings stay authored values; no energy conservation simulation is implied. Shear/nonuniform scale cannot preserve a spherical attenuation field or circular cone in this model, and is rejected before any mutation. Results that overflow also reject. Independent clones do not copy handle/owner/reactors and deep-copy common mutable metadata and XData.

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
var lamp = new Light
{
    Name = "Inspection lamp",
    LightType = LightType.Spot,
    Position = new Vector3(0, 0, 12),
    Target = Vector3.Zero,
    Intensity = 1.25,
    AttenuationType = LightAttenuationType.InverseSquare,
    UseAttenuationLimits = true,
    AttenuationStartLimit = 0.5,
    AttenuationEndLimit = 25,
    HotspotAngle = 35,
    FalloffAngle = 60,
    CastShadows = true,
    ShadowType = LightShadowType.RayTraced
};
document.Entities.Add(lamp);
document.SaveAtomic("inspection.dxf", isBinary: false);
```

## Verification

248 additional registered cases span six typed profiles, text/binary, all three light types, reversed scalar order, text comments, exact nontrivial binary64 intensity, Unicode, dormant values, repeated transport changes, metadata, clones, INSERTs, placement and version rejection, malformed packets, numerical validation and transformation failure nonmutation. Initial test-only mistakes (omitted legacy code-page header and reusing a block-owned clone) were corrected before final validation; no production validation was relaxed. Public APIs are new, so an unchanged-assembly compilation of these API tests is not claimed.

`tools/verify_light.py` loads 24 exports / 48 LIGHT entities with independent ezdxf 1.4.4, checks every published field, common color, WCS points, Unicode name, XData and the following LINE, and requires zero audit errors and repairs. This is independent interoperability evidence, not native rendering certification.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_light.py artifacts/conformance
```

Primary reference: [Autodesk LIGHT DXF reference](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-1A23DB42-6A92-48E9-9EB2-A7856A479930.htm). Scalar-order guidance: [Autodesk common entity codes](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm). The implementation follows the published parameter model; full DXF capability remains incomplete.
