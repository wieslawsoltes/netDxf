# Read-only target-version diagnostics

`document.AnalyzeVersionCompatibility(targetVersion)` reports known version-dependent restrictions and omissions in the current typed writer. It does not change `DrawingVariables.AcadVer`, assign handles, create the lazy object database or named-object root, write output, clone source objects, or run writer preflight. Registered entities in model space, paper space and unused block definitions are included, together with ATTDEF/ATTRIB common metadata and the registered object/table records listed below.

```csharp
DxfVersionCompatibilityReport report = document.AnalyzeVersionCompatibility(DxfVersion.AutoCad2007);
foreach (DxfVersionCompatibilityDiagnostic item in report.Diagnostics)
    Console.WriteLine($"{item.Kind}: {item.SourceHandle} {item.PropertyPath}: {item.Message}");
```

The report captures the current source version, requested target version, diagnostic messages, handles, code names, property paths and summary flags. Its diagnostics collection is immutable. `SourceObject` intentionally references the live inspected `DxfObject`, `HeaderVariables` or `DxfClass`; editing that object does not update the captured fields. Run analysis again after edits. Diagnostics are ordered by captured handle, DXF code name, property path and rule code, using ordinal string ordering.

`HasKnownRejections` means that at least one covered version guard will reject the feature. `HasKnownLosses` means that at least one covered property is omitted by the writer. The report has no `CanSave` or complete-compatibility flag. Other invalid geometry, resource graphs, class definitions, opaque packets, encoding failures and lifecycle errors can still reject a save. It does not certify DXF legality, native CAD acceptance, rendering or application evaluation. No conversion is performed. Unknown and undefined enum values throw `ArgumentOutOfRangeException`; recognized versions before R2000 return the unsupported-writer-profile diagnostic.

## Initial rule matrix

| Rule code | Inspected stored data | Current writer behavior |
|---|---|---|
| `DXF_WRITER_PROFILE_UNSUPPORTED` | Requested target version | Typed writer supports R2000–R2018 |
| `ENTITY_COLOR_NAME_PROFILE` | Entity, ATTDEF and ATTRIB `ColorName`, including empty string | Requires R2004+ |
| `ENTITY_SHADOW_MODE_PROFILE` | Entity, ATTDEF and ATTRIB `ShadowMode`, including explicit zero | Requires R2007+ |
| `MESH_PROFILE` | MESH | Requires R2010+ |
| `HELIX_PROFILE`, `LIGHT_PROFILE`, `SECTION_PROFILE`, `MULTILEADER_PROFILE` | Corresponding entities | Requires R2007+ |
| `LWPOLYLINE_VERTEX_ID_PROFILE` | Each present `Vertexes[i].VertexIdentifier` | Requires R2013+ |
| `MTEXT_BACKGROUND_PROFILE` | Present `BackgroundFill` | Requires R2007+ |
| `MTEXT_FRAME_PROFILE` | Background text-frame flag | Requires R2018+ |
| `MTEXT_DIRECT_COLUMNS_PROFILE` | Direct columns | Requires R2007+ |
| `MTEXT_EMBEDDED_COLUMNS_PROFILE` | Embedded columns | Requires R2018+ |
| `MTEXT_LINKED_COLUMNS_PROFILE` | Legacy linked columns | Requires a target before R2018 |
| `HATCH_SPLINE_FIT_PROFILE` | Each spline edge's nonempty fit points and separately present start/end tangent | Requires R2010+ |
| `MULTILEADER_OPTIONAL_FIELD_PROFILE` | Properties groups 271/272/273, context 272/273 and each leader-node 271 | Requires R2010+ |
| `MULTILEADER_OPTIONAL_FIELD_PROFILE` | Properties group 295 | Requires R2013+ |
| `ACIS_SAT_PROFILE` | Stored BODY/REGION/3DSOLID SAT | Rejects R2013+, where SAB/ACDSDATA regeneration is unavailable |
| `ACIS_HISTORY_PROFILE` | Present 3DSOLID history handle, including `"0"` | Requires R2007+ |
| `VIEW_CAMERA_PROFILE` | Plottable named-view camera | Requires R2007+ |
| `VIEW_LIVE_SECTION_PROFILE` | Present VIEW live-section slot, including null | Requires R2007+ |
| `SUN_OWNER_PROFILE` | Present VIEW/VPORT/VIEWPORT SUN slot, including null | VIEW requires R2010+; other hosts R2007+ |
| `PLOT_SHADE_REFERENCE_PROFILE` | Layout or standalone page-setup shade-plot target | Requires R2007+ |
| `DATATABLE_PROFILE`, `SORTENTSTABLE_PROFILE` | Corresponding typed objects | Requires R2004+ |
| `LIGHTLIST_PROFILE`, `SUN_PROFILE`, `MLEADERSTYLE_PROFILE`, `SECTIONSETTINGS_PROFILE` | Corresponding typed objects | Requires R2007+ |
| `GEODATA_PROFILE` | Typed version-2 GEODATA | Requires R2010+ |
| `STORED_SOURCE_PROFILE` | TABLE, TABLESTYLE, TABLECONTENT, TABLEGEOMETRY, CELLSTYLEMAP, FIELD, DIMASSOC, SECTION_MANAGER, SUNSTUDY and retained POLYLINE and PolygonMesh VERTEX/SEQEND packets | Requires the exact source profile, including when upgrading |
| `HATCH_GRADIENT_OMITTED` | Gradient pattern packet | R2000 omits gradient data and writes the base hatch fill |
| `HEADER_LAST_SAVED_BY_OMITTED` | Nonempty `DrawingVariables.LastSavedBy` | R2000 omits `$LASTSAVEDBY` |
| `CLASS_INSTANCE_COUNT_OMITTED` | Explicit CLASS instance count, including zero | R2000 omits group 91 |

These are concrete existing writer rules, not inferred DXF restrictions. For example, legacy MTEXT defined height remains represented through XData and is not diagnosed as a loss. Proxy-graphics count-code changes preserve the payload and are not omissions. Arbitrary opaque objects are not assigned invented version legality. New typed features must extend this bounded matrix when their writer guards are added; the implementation includes the PR93 CELLSTYLEMAP and ordinary PolygonMesh record storage increments.
