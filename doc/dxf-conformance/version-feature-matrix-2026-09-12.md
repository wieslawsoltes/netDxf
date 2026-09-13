# netDxf: DXF version and feature support audit

Audit date: 12 September 2026. Repository: `wieslawsoltes/netDxf`, branch `netstandard`.

Original source baseline: `5b562312f683fc635405c149537ca488e4ec4d39`.
Updated production baseline: `60f06650a360d13cf325dd4efdaea833670ea457`, after the merged text-hex, UCS metadata and thumbnail PRs #4–#6.

## 1. What support means

netDxf admits six modern DXF database-format families and implements a substantial typed geometry model. It is **not yet a complete AutoCAD DXF preservation or semantic-editing implementation**. Missing records, unmodeled fields, discarded dependencies and absent target-version validation matter more than additional version enum names.

Recognizing `$ACADVER`, decoding a transport, retaining a record and its references, and implementing all its editing semantics are separate capabilities. A LINE round trip proves only its tested subset. Opaque ACIS/ASM preservation would not implement solid modeling; static block appearance does not implement dynamic-block evaluation.

This matrix has **113 feature rows**, each with six version columns. It describes observed implementation paths. A `P` in all six columns means all six formats reach a partial implementation; it does not certify that every emitted field is legal in every historical release. Missing historical introduction dates remain explicit verification work rather than guesses. The broad catalog is Autodesk's 2018 DXF reference, supplemented by current 2026 definitions. This is not an exhaustive census of all undocumented or application-defined classes.

| Mark | Meaning |
|---|---|
| T | Executable regression coverage for the stated subset, not the whole record family. |
| P | Typed/read/write implementation present, incomplete or not comprehensively verified. |
| P! | Serializer exists but a concrete version-admission concern exists. |
| L | Lossy conversion, omission, reduction or regeneration rather than authored-data preservation. |
| O | Tested opaque payload preservation; no semantic evaluation/rendering implied. |
| M | Missing implementation/fallback in this pipeline. Not a claim that the feature existed in every older format. |
| CP / U8 | Observed code-page/Unicode-escape versus UTF-8 strategy. |

The `tools/netDxf.FieldAudit` Roslyn tool generates `field-inventory.json` and `.md`: literal group-code switch cases, write expressions, conditions, dispatch, helper calls, declared model properties, source locations and SHA-256 hashes. This is syntax evidence, not a coverage percentage. A consumed code may be discarded; context, semantics, defaults, dependencies and version legality require fixtures.

## 2. Version profiles

| Format family | `$ACADVER` | Document load/save | Transport | Important source differences |
|---|---|---|---|---|
| AutoCAD 2000 | AC1015 | Admitted; default new-document family | Text and binary | Code-page input/Unicode-escape output strategy. Gradient payload and selected newer class/header fields omitted. Later features are not centrally rejected. |
| AutoCAD 2004 | AC1018 | Admitted | Text and binary | Code-page/escape strategy; gradient output enabled; newer raster/image class counts emitted. |
| AutoCAD 2007 | AC1021 | Admitted | Text and binary | UTF-8 path. Same partial entity/object dispatch, not a complete 2007 schema. |
| AutoCAD 2010 | AC1024 | Admitted | Text and binary | HATCH spline-boundary fit-data branch enabled. Modern MESH is documented from 2010, but its writer is not gated accordingly. |
| AutoCAD 2013 | AC1027 | Admitted | Text and binary | ACDSDATA section recognized and discarded. No complete auxiliary/ASM payload graph. |
| AutoCAD 2018 | AC1032 | Admitted | Text and binary | Same partial modern pipeline; no full current-family record/field certification. |

Autodesk's 2026 HEADER reference still identifies AC1032 as AutoCAD 2018's database version. Product years and database-format families are distinct axes: inventing a 2024 enum does not implement later features carried by AC1032. [S1]

### Historical enum names are not implemented document dialects

| Declared family | Declared identifier | Baseline status |
|---|---|---|
| R1.1 / R1.2 / R1.4 | MC0.0 / AC1.2 / AC1.4 | Rejected by modern document admission; historical spellings/aliases still need authoritative fixtures. |
| R2.0 / R2.10 | AC1.50 / AC2.10 | Rejected. |
| R2.5 / R2.6 | AC1002 / AC1003 | Rejected. |
| R9 / R10 | AC1004 / AC1006 | Rejected. |
| R11–R12 | AC1009 | Rejected; legacy file grammar/transport not implemented here. |
| R13 / R14 | AC1012 / AC1014 | Rejected; no dedicated compatibility serializers. |

`HeaderVariables.AcadVer`, `DxfReader.Read` and the writer impose the 2000 lower bound. The enum is a recognition vocabulary, not an implementation promise. Binary framing must be checked against genuine legacy files, not inferred from the modern reader accepting 16-bit codes.

## 3. Transport and validation

| Feature | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Evidence and remaining scope |
|---|---|---|---|---|---|---|---|
| Text DXF | T | T | T | T | T | T | Version detection and four primitive geometries tested; not every record. `DxfReader.Read`, `DxfWriter.Write`, `DocumentRoundTrip`. |
| Binary DXF: modern two-byte codes | T | T | T | T | T | T | Same six formats; complete sentinel regressions. No legacy framing implementation. `BinaryCodeValueReader/Writer`. |
| Character encoding | CP | CP | U8 | U8 | U8 | U8 | Code-page input/Unicode-escape output versus UTF-8. Custom header strings need independent encoding tests. |
| Text binary-chunk decoding | T | T | T | T | T | T | All 310–319/1004 codes, valid bytes, mixed case, malformed input and missing lines; fixed #4. |
| Ordered unknown-record preservation | M | M | M | M | M | M | Unknown entities/objects/classes and many fields lack a fallback. `ReadEntity`, `ReadObjects`, `ReadClasses`. |
| Strict numeric/handle/boolean parsing | P | P | P | P | P | P | Several malformed values still become zero, false or empty after assertions. `TextCodeValueReader`. |
| Target-version/downgrade diagnostics | M | M | M | M | M | M | No comprehensive legality matrix or per-loss report. `WriteEntity`, `WriteEntityCommonCodes`. |

## 4. Sections

| Section/feature | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Evidence and remaining scope |
|---|---|---|---|---|---|---|---|
| HEADER: typed variables | P | P | P | P | P | P | Typed subset, current UCS and generated active dimension-style fields. `ReadHeader`, `WriteSystemVariable`. |
| HEADER: custom scalars/vectors | P | P | P | P | P | P | Existing CustomValues path; excluded DIM/visual-style variables and arbitrary tag sequences remain gaps. |
| CLASSES | L | L | L | L | L | L | Input definitions discarded; limited raster/image classes regenerated. `ReadClasses`, `WriteImageClass`. |
| TABLES | P | P | P | P | P | P | Seven families implemented, VIEW stubbed, VPORT reduced. `ReadTable`, `ReadTableEntry`. |
| BLOCKS / ENTITIES | P | P | P | P | P | P | Supported models/relationships only, not unknown-content preservation. `ReadBlocks`, `ReadEntity`, `WriteBlock`, `WriteEntity`. |
| OBJECTS | P | P | P | P | P | P | Eleven explicit dispatch cases; no generic ownership graph. `ReadObjects`. |
| THUMBNAILIMAGE | O | O | O | O | O | O | Exact preview bytes added #6; no generation. Empty preview normalizes to absent section. `DxfThumbnailImage`. |
| ACDSDATA | M | M | M | M | M | M | Consumed and discarded; no writer/ASM preservation. `ReadAcdsData`. |
| Unknown/custom sections | M | M | M | M | M | M | No ordered raw section model. `ReadUnknowData`. |

## 5. Entity families

All entity rows inherit the common-field and dependency gaps in section 9. Primitive `T` marks do not certify every optional field. POLYLINE subtypes inside modern documents do not imply support for historical document versions. VIEWPORT entities are distinct from VPORT table records.

| Entity/feature | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Implemented subset and remaining scope |
|---|---|---|---|---|---|---|---|
| LINE | T | T | T | T | T | T | Endpoints and binary XData tested; common fields partial. `ReadLine/WriteLine`. |
| CIRCLE | T | T | T | T | T | T | Center/radius tested; OCS/transforms/optional fields need broader fixtures. `ReadCircle/WriteCircle`. |
| ARC | T | T | T | T | T | T | Center/radius/angles tested; common fields partial. `ReadArc/WriteArc`. |
| POINT | T | T | T | T | T | T | Position tested; all display semantics not certified. `ReadPoint/WritePoint`. |
| 3DFACE | P | P | P | P | P | P | Face3D model; invisible-edge/degenerate cases need tests. `ReadFace3d/WriteFace3D`. |
| SOLID | P | P | P | P | P | P | Planar corners/thickness/normal, not a 3DSOLID. `ReadSolid/WriteSolid`. |
| TRACE | P | P | P | P | P | P | Geometry implemented; full OCS/default behavior unverified. `ReadTrace/WriteTrace`. |
| ELLIPSE | P | P | P | P | P | P | Axis/ratio/parameter model and conversion paths. `ReadEllipse/WriteEllipse`. |
| SPLINE | P | P | P | P | P | P | Control/fit data, knots, weights; all periodic/degenerate cases unverified. `ReadSpline/WriteSpline`. |
| LWPOLYLINE | P | P | P | P | P | P | Bulges/widths/flags/elevation; per-vertex IDs/optional codes need review. `ReadLwPolyline/WriteLwPolyline`. |
| POLYLINE: 2D / VERTEX / SEQEND | P | P | P | P | P | P | Legacy entity representation in modern files, not a legacy file dialect. `ReadPolyline/WritePolyline`. |
| POLYLINE: 3D | P | P | P | P | P | P | Polyline3D topology/flags; transform fixtures needed. `ReadPolyline/WritePolyline`. |
| POLYLINE: polyface mesh | P | P | P | P | P | P | Topology modeled; index/invisible-edge validation needs fixtures. `ReadPolyline/WritePolyline`. |
| POLYLINE: polygon mesh | P | P | P | P | P | P | PolygonMesh and smoothing preprocessing. `ReadPolyline/WritePolyline`. |
| MESH: subdivision mesh | P! | P! | P! | P | P | P | Topology/creases modeled; modern MESH 2010 admission not enforced. Overrides absent. `ReadMesh/WriteMesh`. [S4,S11] |
| RAY | P | P | P | P | P | P | Origin/direction; common fields partial. `ReadRay/WriteRay`. |
| XLINE | P | P | P | P | P | P | Origin/direction; common fields partial. `ReadXLine/WriteXLine`. |
| SHAPE | P | P | P | P | P | P | Number/SHX style resolution; SHX geometry is a separate resource concern. `ReadShape/WriteShape`. |
| TEXT | P | P | P | P | P | P | Text/alignment/style; encoding/OCS corpus incomplete. `ReadText/WriteText`. |
| MTEXT | P | P | P | P | P | P | Content/direction/spacing; background, columns and field dependencies incomplete. `ReadMText/WriteMText`. [S3] |
| INSERT | P | P | P | P | P | P | Block transform/attributes; no dynamic evaluation. `ReadInsert/WriteInsert`. |
| ATTDEF / ATTRIB | P | P | P | P | P | P | Definitions/instances; newer embedded-MTEXT/context variants need audit. `ReadAttributeDefinition/ReadAttribute/WriteAttribute`. |
| DIMENSION: aligned / linear | P | P | P | P | P | P | Geometric models/picture blocks; full association/context graphs absent. `ReadDimension/WriteDimension`. |
| DIMENSION: angular 2-line / 3-point | P | P | P | P | P | P | Typed geometry; all styles/overrides/edge cases unverified. `ReadDimension/WriteDimension`. |
| DIMENSION: diameter / radius / ordinate | P | P | P | P | P | P | Typed geometry; dependency graph incomplete. `ReadDimension/WriteDimension`. |
| ARC_DIMENSION: arc length | P | P | P | P | P | P | Model present; historical eligibility still needs release fixtures. `ReadDimension/WriteDimension`. |
| HATCH: solid / patterned | P | P | P | P | P | P | Loops/patterns/associative contours; complex boundaries/dependencies need fixtures. `ReadHatch/WriteHatch`. |
| HATCH: gradients | L | P | P | P | P | P | Gradient payload omitted for 2000 without explicit loss report. `WriteGradientHatchPattern`. |
| HATCH: spline fit boundary data | L | L | L | P | P | P | Fit-data path has a 2010 threshold. `ReadEdgeBoundaryPath/WriteHatchBoundaryPathData`. |
| MLINE | P | P | P | P | P | P | Vertices/segments/styles; full break/fill/joint semantics not certified. `ReadMLine/WriteMLine`. |
| LEADER | P | P | P | P | P | P | Path/annotation relationships; not MULTILEADER. `ReadLeader/WriteLeader`. |
| TOLERANCE | P | P | P | P | P | P | Geometric tolerance text; all formatting/rendering separate. `ReadTolerance/WriteTolerance`. |
| IMAGE | P | P | P | P | P | P | Reference/definition/clip/reactor subset; not raster rendering. `ReadImage/WriteImage`. |
| DGNUNDERLAY / DWFUNDERLAY / PDFUNDERLAY | P | P | P | P | P | P | References/definitions/clipping; historical admission not comprehensively gated. `ReadUnderlay/WriteUnderlay`. |
| WIPEOUT | P | P | P | P | P | P | Boundary and IO; full variable/dependency coverage not certified. `ReadWipeout/WriteWipeout`. |
| VIEWPORT entity | P | P | P | P | P | P | Paper-space viewport model, distinct from VPORT table. `ReadViewport/WriteViewport`. |
| ACAD_TABLE / TABLE | L | L | L | L | L | L | Imported as INSERT; cells/formulas/formatting/table semantics lost. `ReadAcadTable`. |

### Missing entity families

All rows below are absent from the corresponding dispatch/model path; `M` does not establish eligibility in older releases. SUN and MLEADERSTYLE are nongraphical database objects despite their placement in the reference's entity-topic navigation.

| Family | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Remaining scope |
|---|---|---|---|---|---|---|---|
| HELIX | M | M | M | M | M | M | Helix model and IO. |
| MULTILEADER | M | M | M | M | M | M | Multi-context leader model; MLEADERSTYLE object also absent. |
| LIGHT | M | M | M | M | M | M | Light entity; SUN is a separate missing object. |
| SECTION | M | M | M | M | M | M | Section-plane entity and dependency graph. |
| REGION / BODY / 3DSOLID | M | M | M | M | M | M | ACIS entity/payload preservation, followed by separate semantic geometry integration. |
| SURFACE: base / extruded / lofted / revolved / swept | M | M | M | M | M | M | Surface schemas and complete ACIS/ASM dependency preservation. |
| OLEFRAME / OLE2FRAME | M | M | M | M | M | M | Inert embedded OLE payload preservation. |
| ACAD_PROXY_ENTITY | M | M | M | M | M | M | Class/unknown subclass/proxy payload preservation. |
| COORDINATION_MODEL | M | M | M | M | M | M | Coordination-model entity/definition dependencies. |

## 6. Symbol tables

| Table | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Evidence and remaining scope |
|---|---|---|---|---|---|---|---|
| APPID | P | P | P | P | P | P | Registry/XData registration. `ReadApplicationId/WriteApplicationRegistry`. |
| BLOCK_RECORD | P | P | P | P | P | P | Block ownership/layout links; all flags/dependencies unverified. `ReadBlockRecord/WriteBlockRecord`. |
| DIMSTYLE | P | P | P | P | P | P | Large typed subset, not a complete versioned override catalog. `ReadDimensionStyle/WriteDimensionStyle`. |
| LAYER | P | P | P | P | P | P | Layer/style/color subset; material/plot-style graphs missing. `ReadLayer/WriteLayer`. |
| LTYPE | P | P | P | P | P | P | Simple/complex text/shape segments; resources/missing-style cases need fixtures. `ReadLinetype/WriteLinetype`. |
| STYLE | P | P | P | P | P | P | Text/shape styles; defaults/obsolete fields need review. `ReadTextStyle/WriteTextStyle/WriteShapeStyle`. |
| UCS | P | P | P | P | P | P | Origin/axes; code 146 consumed but ignored. Table XData fixed #5. `ReadUCS/WriteUCS`. |
| VIEW | M | M | M | M | M | M | Existing class is not IO: reader returns null, collection internal, writer emits no entries. `ReadView`, `DxfDocument.Views`. |
| VPORT | L | L | L | L | L | L | Only first *Active configuration retained; other records/many fields discarded. `ReadTableEntry/ReadVPort/WriteVPort`. |

## 7. OBJECTS

Eleven explicit `ReadObjects` dispatch cases do not mean eleven complete schemas. Three underlay definitions count separately; IMAGEDEF_REACTOR is regenerated; XRECORD primarily bridges private layer-state data. PlotSettings embedded in a LAYOUT does not imply standalone PLOTSETTINGS support.

| Object/feature | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Evidence and remaining scope |
|---|---|---|---|---|---|---|---|
| DICTIONARY | P | P | P | P | P | P | Known named collections, not arbitrary nested extension dictionaries. `ReadDictionary/WriteDictionary`. |
| GROUP | P | P | P | P | P | P | Named groups/entity references. `ReadGroup/WriteGroup`. |
| LAYOUT | P | P | P | P | P | P | Model/paper layouts and embedded plot settings subset. `ReadLayout/WriteLayout`. |
| MLINESTYLE | P | P | P | P | P | P | Multiline styles, not multileader styles. `ReadMLineStyle/WriteMLineStyle`. |
| IMAGEDEF | P | P | P | P | P | P | Definitions/selected dictionaries. `ReadImageDefinition/WriteImageDef`. |
| IMAGEDEF_REACTOR | L | L | L | L | L | L | Independent authored object not retained; regenerated. `ReadImageDefReactor/WriteImageDefReactor`. |
| RASTERVARIABLES | P | P | P | P | P | P | Raster display variables. `ReadRasterVariables/WriteRasterVariables`. |
| DGNDEFINITION / DWFDEFINITION / PDFDEFINITION | P | P | P | P | P | P | Three explicit underlay object cases. `ReadUnderlayDefinition/WriteUnderlayDefinition`. |
| XRECORD | L | L | L | L | L | L | Private cache/layer states, not generic public ownership graph. `ReadXRecord/ReadLayerState/WriteLayerState`. |
| PLOTSETTINGS standalone | M | M | M | M | M | M | Type exists in LAYOUT; standalone OBJECTS dispatch absent. `ReadObjects/ReadPlotSettings`. |

### Missing object families

These are absent from the audited object pipeline. Related families are grouped; dynamic/application-defined schemas require a separate class census.

| Family | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Remaining scope |
|---|---|---|---|---|---|---|---|
| ACAD_PROXY_OBJECT / ACDBPLACEHOLDER | M | M | M | M | M | M | Proxy/class/payload fidelity. |
| ACDBDICTIONARYWDFLT / DICTIONARYVAR | M | M | M | M | M | M | Default dictionaries and typed dictionary variables. |
| ACDBNAVISWORKSMODELDEF | M | M | M | M | M | M | Coordination-model definition. |
| DATATABLE | M | M | M | M | M | M | Object data table schema. |
| DIMASSOC | M | M | M | M | M | M | Associative dimension records/links. |
| FIELD | M | M | M | M | M | M | Field expression/evaluation records. |
| GEODATA | M | M | M | M | M | M | Georeferencing object. |
| IDBUFFER / OBJECT_PTR | M | M | M | M | M | M | Pointer-container records. |
| LAYER_FILTER / LAYER_INDEX | M | M | M | M | M | M | Layer filters/indexes. |
| LIGHTLIST | M | M | M | M | M | M | Light-object lists. |
| MATERIAL | M | M | M | M | M | M | Materials/textures/mappers and references. |
| RENDERSETTINGS / MENTALRAYRENDERSETTINGS | M | M | M | M | M | M | Rendering setting families. |
| RENDERENVIRONMENT / RENDERGLOBAL | M | M | M | M | M | M | Environment/global records. |
| RAPIDRTRENDERENVIRONMENT / RAPIDRTRENDERSETTINGS | M | M | M | M | M | M | RapidRT families. |
| SECTION manager/settings/type/geometry objects | M | M | M | M | M | M | Section-settings graph. |
| SORTENTSTABLE | M | M | M | M | M | M | Draw-order records. |
| SPATIAL_FILTER / SPATIAL_INDEX | M | M | M | M | M | M | Spatial clipping/filter/index graph. |
| SUN / SUNSTUDY | M | M | M | M | M | M | Sun configuration/study objects. |
| TABLESTYLE | M | M | M | M | M | M | Table style schema. |
| VBA_PROJECT | M | M | M | M | M | M | Inert VBA payload preservation, not execution. |
| VISUALSTYLE | M | M | M | M | M | M | Visual-style records and dependent references. |
| WIPEOUTVARIABLES | M | M | M | M | M | M | Standalone wipeout settings. |

## 8. Header-variable fidelity

The explicit `ReadHeader` switch covers the following names, checked against the header declarations:

`$ACADVER`, `$HANDSEED`, `$ANGBASE`, `$ANGDIR`, `$ATTMODE`, `$AUNITS`, `$AUPREC`, `$CECOLOR`, `$CELTSCALE`, `$CELTYPE`, `$CELWEIGHT`, `$CLAYER`, `$CMLJUST`, `$CMLSCALE`, `$CMLSTYLE`, `$DIMSTYLE`, `$TEXTSIZE`, `$TEXTSTYLE`, `$LASTSAVEDBY`, `$LUNITS`, `$LUPREC`, `$DWGCODEPAGE`, `$EXTNAMES`, `$INSBASE`, `$INSUNITS`, `$LTSCALE`, `$LWDISPLAY`, `$MIRRTEXT`, `$PDMODE`, `$PDSIZE`, `$PLINEGEN`, `$PSLTSCALE`, `$SPLINESEGS`, `$SURFU`, `$SURFV`, `$TDCREATE`, `$TDUCREATE`, `$TDUPDATE`, `$TDUUPDATE`, `$TDINDWG`, `$UCSORG`, `$UCSXDIR`, `$UCSYDIR`.

**Custom-variable preservation already exists.** `HeaderVariables.CustomValues`, `AddCustomVariable` and `TryGetCustomVariable` retain many unmodeled scalar and Vector2/Vector3 values. It would be incorrect to claim that all unknown header variables are discarded.

Remaining differences:

- Unhandled `$DIM*` variables and two interference visual-style references are deliberately skipped. The writer generates selected active-DIMSTYLE values instead of retaining independent authored header overrides.
- `$ACADMAINTVER` is intentionally ignored; Autodesk recommends ignoring it. Its omission alone is not a bug. [S1]
- Arbitrary multi-tag values, original order and duplicate occurrences are not represented by the scalar/vector dictionary. Duplicate custom names become the last value.
- Custom strings do not take exactly the same encoding/decoding path as modeled strings. Non-ASCII round trips need explicit pre-2007 and UTF-8 fixtures.
- Retaining hexadecimal text does not retain an unsupported referenced object's graph.

## 9. Common fields and graphs

| Feature | 2000 | 2004 | 2007 | 2010 | 2013 | 2018 | Evidence and remaining scope |
|---|---|---|---|---|---|---|---|
| Layer/linetype/ACI/lineweight/scale/visibility | P | P | P | P | P | P | Implemented common subset; defaults/off-layer semantics need fixtures. `ReadEntity/WriteEntityCommonCodes`. |
| RGB true color | P | P | P | P | P | P | 420 emitted without complete version policy; TrueColor appears in 2004 API history. [S10] |
| Transparency | P | P | P | P | P | P | 440 path exists; version eligibility/all flags unverified. |
| Material / plot-style references | M | M | M | M | M | M | Common 347/390 graph relationships incomplete. |
| Color names / shadow mode / proxy graphics | M | M | M | M | M | M | Common 430,284,92/310 paths absent. |
| XData basic typed records | P | P | P | P | P | P | APPID/records implemented; clone/transform/quotas/remapping need deeper tests. |
| XData binary round trips | T | T | T | T | T | T | Exact bytes/chunk counts across all formats and transports, #4. |
| Extension dictionaries / persistent reactors | P | P | P | P | P | P | Selected relationships; arbitrary dependency closure absent. |
| Dynamic blocks / annotation / associative networks | M | M | M | M | M | M | Static appearance may survive; graphs/evaluation absent. |
| Unknown application/subclass fields | M | M | M | M | M | M | No universal same-version fallback. |

### Concrete field-level findings

| Area | Observation | Required change |
|---|---|---|
| Common entity | Common writer emits 0,5,6,8,48,60,62,67,100,102,330,370,420,440. | Complete 347 material,390 plot-style,430 name,284 shadow,92/310 graphics and their dependencies. [S2] |
| Extension dictionary | Selected 102/reactor paths do not implement arbitrary `{ACAD_XDICTIONARY}`/360 closure. | Ordered control groups, ownership kinds and hard/soft reference semantics. [S2,S6] |
| MTEXT background | Complete background context absent. | 90 flags,45 fill scale,63/color fields; preserve authored values. [S3] |
| MTEXT columns | Full column context absent; code 50 is reused. | Type/count/flow/auto-height/width/gutter/heights; do not interpret column data as ordinary rotation. [S3] |
| MTEXT derived fields | Standard marks 42/43 derived/read-only and ignored on input. | Do not classify every absent read case as a semantic defect; preserve/recompute according to fidelity contract. [S3] |
| MESH overrides | Core topology/creases exist, subsequent override grammar absent. | Parse repeated 90/91/92 in a distinct context, not as topology counts. [S4] |
| UCS | Elevation 146 consumed but ignored. | Assign/model/serialize elevation and review optional UCS state. |
| VIEW | ReadView returns null; empty output table. | Named views, center/camera/target/clip/modes/UCS plus supported references. [S5] |
| VPORT | Only selected first-*Active state copied. | Retain all entries and complete state, independently of VIEWPORT entities. |
| HATCH downsave | Gradient payload omitted in AC1015. | Explicit diagnostic or caller-selected fallback, not implied fidelity. |

## 10. Delivered fixes and regression evidence

| PR | Scope | Evidence |
|---|---|---|
| #1 | Baseline harness/CI from concurrent work. | Merged; infrastructure is not standard completeness. |
| #2 | Duplicate baseline from this session. | Closed unmerged after detecting #1; not delivered work. |
| #3 | Full 22-byte binary sentinel validation from concurrent work. | Merged; regressions retained. |
| #4 | Strict text hexadecimal chunks; remove silent empty payloads/incidental exceptions. | Red 105 pass/44 fail; green149/0; Linux/Windows Debug/Release and netstandard2.0 build pass. Merged. |
| #5 | UCS TABLE mistakenly copied BLOCK_RECORD XData. | Red149/12; green161/0; all four CI jobs pass. Merged. |
| #6 | THUMBNAILIMAGE byte preservation. | Old IO with new helper/API193/12; integrated205/0; all four CI jobs pass. Merged. |

PR #6's downloaded CI source and result artifacts were verified against locally compiled signed-library sources. Merging source does not imply package publication or independent AutoCAD qualification. These counts apply to the pinned snapshot and must not be treated as a completeness metric.

### Open source-level findings

| Finding | Evidence | Required proof |
|---|---|---|
| Short binary chunks | `ReadBytes(length)` lacks an exact-length check in this baseline. | All truncation boundaries/fragmented reads; coordinate with binary-codec work. |
| Numeric/handle/boolean defaulting | Several text parsers assert/substitute values. | Strict/tolerant contract with contextual errors; Debug and Release regressions. |
| Separate binary format probe | `DxfReader.IsBinary` has its own sentinel logic. | Short buffers, full sentinel, position restoration, stream ownership. |
| File cleanup | Debug exception paths need structured-disposal audit. | Throwing/locked-file regressions without closing caller streams. |
| Structural EOF loops | ENDSEC/ENDTAB/sequence loops plus synthesized EOF. | Bounded termination for malformed sections/tables/unknown records. |
| XData binary cloning | Clone passes mutable record values through. | Reproduce mutation aliasing, then isolate fix. |
| Custom header encoding | Bypasses modeled-string codec path. | Non-ASCII fixtures across all six versions and both transports. |

These are findings, not completed fixes. Obsolete advisory values such as TABLE code70 are not bugs solely because they differ from an inferred count; verify documented semantics first.

## 11. Architecture and remaining implementation sequence

A lossless record layer must sit underneath typed objects: ordered tags, subclasses, 102 groups, class/record identities, original version and opaque bytes. Typed projections should retain unfamiliar fields instead of deleting them.

The graph must distinguish handles from ordinary strings, pointers from owners, hard from soft references, and external resources from embedded payloads. Handle allocation must reserve opaque IDs. Clone/import/merge must remap complete closures rather than copy unknown hexadecimal references blindly.

Version profiles need record/property-level introduction/removal, wire types, defaults, ranges and downgrade rules. Saving should select preserve, reject-unsupported or explicit lossy conversions and report every loss. Table-to-INSERT and gradient-to-flat conversions must not masquerade as fidelity.

Opaque preservation and interpretation are separate milestones. Preserve complete ACIS/ASM payload graphs before geometry integration. Preserve OLE/VBA inertly; never execute payloads during load/audit. Dynamic blocks, fields, associativity and annotation contexts require schema and evaluation contracts.

| Workstream | Version coverage | Feature-sized PR order | Acceptance |
|---|---|---|---|
| Codecs/structure | Six modern formats, then legacy | Exact reads; numeric errors; EOF termination; Unicode; quotas. | Positive/malformed/fragmented-stream corpus, no hangs, ownership preserved. |
| Raw records/graphs | AC1032 down to AC1015 | Ordered tags/sections/classes; unknown entities/objects; dictionaries; remapping. | Same-version data and reference closure, not known geometry alone. |
| AC1015 | 2000 | Complete baseline tables/entities/common fields and reject/downgrade later features. | Native AC1015 fixtures and emitted-code legality. |
| AC1018 | 2004 | Verified color/gradient/other additions; code-page behavior. | Property eligibility, Unicode and explicit downsave losses. |
| AC1021 | 2007 | UTF-8 plus release-specific entities/objects/context additions. | Unicode corpus; actual historical admission recorded. |
| AC1024 | 2010 | Mesh/underlay/boundary additions and dependencies. | Topology/override fixtures and lower-format policy. |
| AC1027 | 2013 | ACDSDATA schemas/records and payload ownership. | Opaque closure plus separately tested semantic adapters. |
| AC1032 | 2018 and verified later extensions | Published entity/object/property catalog and extensions. | Differential CAD import/export and unresolved-class accounting. |
| AC1012/AC1014 | R13/R14 | Dedicated grammar, handles/subclasses/transport profiles. | Genuine legacy fixtures and downsave reports. |
| AC1009 and earlier | R11/R12 and older | Work backward through documented tables/entities/encoding/framing. | No enum-only claims; independent fixtures per dialect. |

Concrete feature ledger: VIEW; VPORT collections; UCS elevation; MTEXT background; MTEXT columns; MESH overrides; structured TABLE/TABLESTYLE; HELIX; MULTILEADER/MLEADERSTYLE; FIELD; DIMASSOC; GEODATA; MATERIAL; VISUALSTYLE; light/sun/section families; generic XRECORD/dictionary variants; draw order/spatial filtering; proxy/classes; ACIS/ASM; ACDSDATA; OLE/VBA; dynamic/annotation/association graphs; historical profiles and downgrade paths. These are dependencies and work units, not declarations of implementation.

Each semantic family belongs in its own PR, merged after its applicable matrix passes. Independent work may proceed in parallel; shared reader/writer/graph edits must be coordinated against the current default branch.

## 12. Reproduction and completion criteria

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
dotnet build netDxf/netDxf.csproj -f netstandard2.0 -c Release
dotnet run --project tools/netDxf.FieldAudit -- --self-test
dotnet run --project tools/netDxf.FieldAudit -- . artifacts/field-audit
```

Production retains `netstandard2.0`, `net471`, `net48`, `net6.0`, `net8.0`, assembly identity and signing. Current CI executes .NET8 and builds netstandard2.0; it does not execute every legacy runtime target. Release qualification must add consumer/runtime checks without removing compatibility to simplify testing.

Full capability requires independently authored files for every admitted version and applicable record/property, semantic assertions, opaque-byte checks, ownership closure, malformed input, Unicode, extremes, large files and resource limits. Matching reader/writer bugs can pass self-round trips. AutoCAD open/AUDIT/save/reopen, data extraction and visual comparison are separate evidence; visual agreement does not prove formulas, fields or graphs survived.

At this snapshot full standard capability remains unachieved. The delivered code and evidence are concrete, and the remaining gaps are enumerated. No completeness percentage or certification is inferred from test count.

## 13. Primary references

Repository source: https://github.com/wieslawsoltes/netDxf/tree/60f06650a360d13cf325dd4efdaea833670ea457 . Main evidence: `DxfVersion`, `HeaderVariables`, `DxfDocument`, `IO/*.cs`, entity/table/object models and conformance tests. Generated evidence provides source lines/hashes.

- S1 HEADER/format identifiers: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm
- S2 Common entity fields/order: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm
- S3 MTEXT: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm
- S4 MESH: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm
- S5 VIEW: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-CF3094AB-ECA9-43C1-8075-7791AC84F97C.htm
- S6 Numerical group codes: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm
- S7 Published catalog: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/index.htm
- S8 THUMBNAILIMAGE: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-F0369984-9699-40D5-8F9A-139491A14231.htm
- S9 XData: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A2A628B0-3699-4740-A215-C560E7242F63.htm
- S10 AutoCAD2004 API additions: https://help.autodesk.com/cloudhelp/2023/CHS/AutoCAD-ActiveX/files/GUID-82A124B3-1F6D-44EF-A3AE-CA269A5BC5CB.htm
- S11 Modern mesh generation from2010: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-A6232957-5039-4AB7-8B1D-8FD0AD98F77B.htm
- S12 Binary DXF: https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-FC1C3C69-DBC2-49E4-893A-000D6538C0FE.htm
- S13 Sun object ownership: https://help.autodesk.com/cloudhelp/2020/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Sun.html
