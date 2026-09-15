# Stored SUN and reciprocal ownership

`DxfSun` models the version-one `AcDbSun` OBJECTS packet. Its `Sun` attachment is exposed on VPORT table records, VIEW table records and VIEWPORT entities. These are stored settings: no sun-position computation, calendar conversion, illumination or rendering is performed.

The admitted VPORT/VIEWPORT profile begins at R2007. Named VIEW ownership is conservatively gated at R2010, matching the independent producer's explicit field-version declaration. Earlier SUN OBJECTS packets and unknown versions, subclasses or private fields retain the whole object payload as `DxfOpaqueObject`; an attached opaque SUN still prevents owner deletion or cloning that would lose it.

## Schema and evidence

The primary [Autodesk SUN DXF table](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-BB191D89-9302-45E4-9904-108AB418FAE1.htm) gives the core group mapping. Native SUN records are in OBJECTS and carry `AcDbSun`, without an `AcDbEntity` subclass, despite the documentation's entity index placement.

| Group | Public property | Admitted representation |
| --- | --- | --- |
| 90 | StoredVersion | Exactly 1 |
| 290 | Enabled | Boolean |
| 63 | ColorIndex | Independent indexed color, 0–256 |
| 421 | TrueColor | Optional 24-bit RGB, retained independently of 63 |
| 40 | Intensity | Finite double; no undocumented sign restriction |
| 291 | ShadowsEnabled | Boolean |
| 91 | JulianDay | Raw signed 32-bit integer |
| 92 | StoredTime | Raw signed 32-bit integer; no unit conversion |
| 292 | DaylightSavingTime | Boolean |
| 70 | ShadowType | RayTraced 0, ShadowMaps 1, AreaSampled 2 |
| 71 | ShadowMapSize | 64, 128, 256, 512, 1024, 2048 or 4096 |
| 280 | ShadowSoftness | Unsigned byte, 0–255 |

The SUN table labels group 92 as seconds past midnight. The pinned native packets store `54000000`; the primary ObjectARX [AcDbSun.dateTime](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbSun__dateTime.html) exposes `AcDbDate`, whose [msecsPastMidnight](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbDate__msecsPastMidnight.html) is explicitly milliseconds. `StoredTime` preserves the integer and makes no claim that the contradictory DXF label defines a conversion.

The SUN DXF table lists only shadow types 0 and 1. The primary [AcGiShadowParameters.ShadowType](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcGiShadowParameters__ShadowType1.html) also includes `kAreaSampled` at 2; both pinned native packets store 2. The [shadow-map size setter](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcGiShadowParameters__setShadowMapSize_Adesk__UInt16.html) explicitly lists the seven allowed sizes. The [softness setter](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcGiShadowParameters__setShadowMapSoftness_Adesk__UInt8.html) accepts an unsigned byte; no speculative 0–10 restriction is imposed.

[VIEW group 361](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-CF3094AB-ECA9-43C1-8075-7791AC84F97C.htm) is explicitly a Sun hard ownership ID. [VIEWPORT group 361](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-2602B0FB-02E4-4B9A-B03C-B1D904753D34.htm) is the optional Sun ID. The current [VPORT page](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-8CE7CC87-27BD-4490-89DA-C21F516415A9.htm) omits this field, so VPORT qualification additionally relies on the native reciprocal VPORT22 → SUN23 packets and the independent implementation. The common SUN owner must identify the same physical retained owner as group 361; runtime resources synthesized at an unrelated handle cannot satisfy it.

The independent [ezdxf 1.4.4 SUN implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/sun.py) supports R2007 export and optional 421. Its SUN shadow validator admits only 0/1, so its typed shadow validation is not an oracle for native value 2. Its [VPORT](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/vport.py), [VIEW](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/view.py) and [VIEWPORT](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/viewport.py) declare Sun handles, with named VIEW starting at R2010. The raw tag grammar in `tools/verify_sun.py` verifies these packets independently and reports ancillary audits separately.

## Ownership operations

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
var owner = document.Views.Add(new View("Exterior"));
var sun = new DxfSun
{
    Enabled = true,
    StoredTime = 54000000,
    ShadowType = DxfSunShadowType.AreaSampled
};
document.Objects.SetSun(owner, sun);

var destination = document.Views.Add(new View("ExteriorCopy"));
var copy = document.Objects.CloneSun(sun, destination);
document.Objects.EraseOwnedTree(copy); // Also clears destination.Sun.
```

Only physical hosts registered in the document object registry are admitted. In particular, a paper-space layout’s retained-only overall viewport (`Layout.Viewport`, typically Id1) is outside this increment when it is absent from that registry; typed SUN source ownership by such an unregistered carrier is rejected. The reader recognizes group 361 only in the public host subclass, outside private control groups and before XData.

`SetSun` requires a registered eligible owner with an empty slot and a detached, unerased typed SUN. `CloneSun` copies the entire typed ownership subtree, including extension dictionaries, XRECORDs and common metadata. It automatically maps the source owner to the registered destination owner. Other cross-document references require an explicit mapping; mapping enumeration and validation finish before handle allocation. Stored APPID metadata copying does not call overridable public cloning methods.

Ordinary `owner.Clone()` rejects an attached SUN, because it cannot return a detached host while retaining the registered ownership graph safely. Create the destination host, copy its desired view/viewport scalar settings, register it, then call `CloneSun`. This increment does not provide a combined host-plus-SUN cloning API.

`EraseOwnedTree` checks the complete owned subtree and all exposed incoming references, then clears the reciprocal SUN slot at commit. Other pointers to the SUN or its descendants reject erasure before mutation. Opaque descendants require their application schema and reject typed erasure/cloning. Erasure is terminal. Ordinary collection removal of a host remains blocked until its SUN subtree has been explicitly erased.

## Fixtures and qualification

The pinned `acad_table_simple.dxf.gz` and `acad_table_with_blk_ref.dxf.gz` inputs use the source hashes already recorded in `tools/table_oracle/fixtures.json`. Each contains SUN23 with common owner22, VPORT22 with group 361→23, raw time 54000000, shadow type 2 and RGB 16777215. Their native CLASS is `SUN / AcDbSun / SCENEOE`, flags 1153, non-graphical. The writer retains compatible class metadata and updates the physical instance count.

The focused fixtures extract each SUN packet into an otherwise controlled R2007 document. They disclose the SUN identity relocation to 7F000 and external common-owner relocation to the controlled VPORT; every subclass field, ordering and floating-point bit pattern remains exact. These extractions qualify the SUN body and reciprocal relationship, not all unrelated fields of the original drawing.

The mandatory independent output inventory is 22 authored profile/owner/transport files plus four native files. Tests also cover scalar boundaries, optional truecolor, all three owner families, reciprocal lookup, null and normalized identities, private fallback, malformed public fields, profile rejection, explicit clone/remap, incoming-reference protection, terminal erasure and attachment atomicity. No native CAD execution or all-values native acceptance is claimed.

The exact focused receipt is recorded in [sun-qualification.json](sun-qualification.json): final source 4a1b7e9 passes 867 Debug cases, including 300 SUN tests and 66 private host context assertions, with 26 independent outputs and zero audits or repairs. The separately pinned earlier Release checkpoint passed 313 focused cases; the integration branch records the final combined Release matrix.
