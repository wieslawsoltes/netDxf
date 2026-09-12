# UCS table elevation

Add `UCS.Elevation`, the named user-coordinate-system table record value stored in DXF group 146. The default is zero; finite positive and negative elevations are supported. Non-finite API assignments are rejected without changing the prior value. Elevation is independent of `Origin` and the UCS axes/transformation.

The reader now assigns the previously discarded value; the writer emits it; both clone overloads preserve it. All six admitted modern document versions share this path. This does not implement the still-unmodeled orthographic-origin pairs, xref metadata or base-UCS ownership graph.

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
var coordinateSystem = new UCS("Level2") { Elevation = 3.25 };
document.UCSs.Add(coordinateSystem);
document.Save("elevated.dxf");
```

## Verification

39 new registered tests cover API defaults, finite-value validation, clone/origin independence, five elevation values in each version/transport combination, exact emitted UCS group-146 values, manually authored semantic UCS records with reordered fields, omitted elevation defaults, axes and handles. The handcrafted fixture does not use `WriteUCS` to construct its UCS record.

With the new model API but the old reader/writer, local execution reports **350 passed / 24 failed**. The integrated signed library reports **374 passed / 0 failed** in Debug and Release. Final-head cross-platform CI remains required before merge.

Minimal-input investigation also found a separate null-layout failure when OBJECTS is absent; it is not fixed or claimed resolved by this feature. The authored fixtures here contain a root dictionary so they isolate UCS field behavior.

## Primary reference

Autodesk UCS group-code definition, including 146:
https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-1906E8A7-3393-4BF9-BD27-F9AE4352FB8B.htm

This closes the elevation-specific gap recorded in the source-pinned version/feature matrix, not complete UCS or AutoCAD conformance.
