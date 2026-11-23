# netDxf Rust Port — Progress & Architecture Tracking

This document tracks the idiomatic, high-performance Rust port of the `netDxf` core library and test suite (`rust/netdxf`), following the FerroUI porting guide principles (`docs/porting/PORTING-GUIDE.md` and `docs/idiomatic-rust.md`) while keeping the upstream `netDxf` file-system layout and public API naming closely aligned.

## Architectural Principles

1. **1:1 File-System & Module Structure**: Mirrors `netDxf/` directories and files in `snake_case` (`header/`, `tables/`, `blocks/`, `entities/`, `objects/`, `collections/`, `units/`, `io/`, and root modules `vector2.rs`, `vector3.rs`, `aci_color.rs`, `dxf_document.rs`, `math_helper.rs`, etc.).
2. **Recognizable Public API**: Preserves upstream `netDxf` type names (`DxfDocument`, `DxfObject`, `EntityObject`, `HeaderVariables`, `AciColor`, `Vector2`, `Vector3`, `Matrix3`, `MathHelper`, `Layer`, `Linetype`, `TextStyle`, `DimensionStyle`, `UCS`, `View`, `VPort`, `Block`, `Polyline2D`, `Spline`, `Hatch`, `Insert`, `DxfReader`, `DxfWriter`, etc.) and `snake_case` member names.
3. **100% Safe Rust (`#![forbid(unsafe_code)]`)**: Zero `unsafe` blocks anywhere in the workspace.
4. **Idiomatic Rust Types (No CLR/BCL Emulation)**:
   - `Result<T, DxfError>` (`thiserror`) instead of `.NET` exception hierarchies.
   - `Option<T>` instead of nullable references / `Nullable<T>`.
   - `Vec<T>`, `IndexMap<K, V>` (`TableObjects<T>`) instead of `.NET` `List<T>` / `ObservableDictionary<K, V>`.
   - `bitflags!` for `[Flags]` enums (`LayerFlags`, `BlockTypeFlags`, `AttributeFlags`, `PolylineTypeFlags`, `SplineTypeFlags`, `HatchBoundaryPathTypeFlags`, `ViewportStatusFlags`, `Face3DEdgeFlags`, `ImageDisplayFlags`, `UnderlayDisplayFlags`, `MLineFlags`, `MLineStyleFlags`, `TextStyleFlags`, `FontStyle`).
   - `std::time::SystemTime` / `std::time::Duration` + `DrawingTime` Julian calendar conversions.
   - `Send + Sync + Clone + Debug + PartialEq` on all core types (zero `Rc<RefCell<...>>` cycles).
5. **High Performance**:
   - Reusable byte buffers in `TextCodeValueReader` and `BinaryCodeValueReader`.
   - Buffered I/O (`BufReader`, `BufWriter`) with direct typed tag read/write paths.
   - Boxed large `EntityObject` variants for compact contiguous `Vec<EntityObject>` storage.
   - Release profile with `lto = "thin"`, `codegen-units = 1`, `opt-level = 3`.

## Upstream-to-Rust Type Map

| Upstream C# | Rust | Notes |
|---|---|---|
| `DxfVersionNotSupportedException`, `ArgumentException`, etc. | `netdxf::DxfError` | Implements `std::error::Error` via `thiserror`. |
| `System.DateTime` / `System.TimeSpan` | `std::time::SystemTime` / `Duration` + `units::DrawingTime` | Exact DXF Julian calendar serial & elapsed-day conversions. |
| `System.Drawing.Color` | `[u8; 3]` RGB helpers on `AciColor` | Platform-independent, zero GUI dependency. |
| `netDxf.Collections.TableObjects<T>` | `netdxf::collections::TableObjects<T>` | Case-insensitive `IndexMap`-backed table preserving insertion order. |
| `netDxf.Entities.EntityObject` (abstract class) | `netdxf::entities::EntityObject` (enum) + `EntityCommon` | Zero-GC, `Send + Sync`, exhaustive pattern matching. |
| `netDxf.Tables.TableObject` (abstract class) | `netdxf::tables::TableObject` trait + `TableObjectData` | Shared name validation, reserved flag, `DxfObject`. |

## Module & File Inventory

| Rust Path | Upstream C# Path | Status |
|---|---|---|
| `rust/netdxf/src/error.rs` | `IO/DxfVersionNotSupportedException.cs` & exceptions | Complete |
| `rust/netdxf/src/coordinate_system.rs` | `CoordinateSystem.cs` | Complete |
| `rust/netdxf/src/vector2.rs`, `vector3.rs`, `vector4.rs` | `Vector2.cs`, `Vector3.cs`, `Vector4.cs` | Complete |
| `rust/netdxf/src/matrix2.rs`, `matrix3.rs`, `matrix4.rs` | `Matrix2.cs`, `Matrix3.cs`, `Matrix4.cs` | Complete |
| `rust/netdxf/src/math_helper.rs` | `MathHelper.cs` | Complete |
| `rust/netdxf/src/bounding_rectangle.rs` | `BoundingRectangle.cs` | Complete |
| `rust/netdxf/src/clipping_boundary.rs`, `clipping_boundary_type.rs` | `ClippingBoundary.cs`, `ClippingBoundaryType.cs` | Complete |
| `rust/netdxf/src/bezier_curve_quadratic.rs`, `bezier_curve_cubic.rs` | `BezierCurveQuadratic.cs`, `BezierCurveCubic.cs` | Complete |
| `rust/netdxf/src/aci_color.rs` | `AciColor.cs` | Complete |
| `rust/netdxf/src/transparency.rs` | `Transparency.cs` | Complete |
| `rust/netdxf/src/lineweight.rs` | `Lineweight.cs` | Complete |
| `rust/netdxf/src/dxf_object_code.rs`, `subclass_marker.rs`, `symbols.rs` | `DxfObjectCode.cs`, `SubclassMarker.cs`, `Symbols.cs` | Complete |
| `rust/netdxf/src/dxf_object.rs`, `dxf_class.rs` | `DxfObject.cs`, `DxfClass.cs` | Complete |
| `rust/netdxf/src/x_data.rs`, `x_data_code.rs`, `x_data_record.rs` | `XData.cs`, `XDataCode.cs`, `XDataRecord.cs` | Complete |
| `rust/netdxf/src/dxf_document.rs` | `DxfDocument.cs` | Complete (Core API, Load/Save ASCII & Binary) |
| `rust/netdxf/src/units/*.rs` | `Units/*.cs` | Complete (DrawingUnits, DrawingTime, UnitHelper, unit enums) |
| `rust/netdxf/src/gte/*.rs` | `GTE/*.cs` | Complete (GVector, GMatrix, BandedMatrix, GaussianElimination, Integration, RootBisection, BasisFunction, BSplineCurve/Surface, NURBSCurve/Surface, BSplineCurveFit, BSplineSurfaceFit) |
| `rust/netdxf/src/header/*.rs` | `Header/*.cs` | Complete (DxfVersion, AttMode, PointShape, HeaderVariables) |
| `rust/netdxf/src/tables/*.rs` | `Tables/*.cs` | Complete (ApplicationRegistry, Layer, Linetype, TextStyle, ShapeStyle, UCS, View, VPort, DimensionStyle, DxfTableCellAddress, DxfTableFormula, DxfTableCalculation) |
| `rust/netdxf/src/blocks/*.rs` | `Blocks/*.cs` | Complete (Block, BlockRecord, BlockTypeFlags, EndBlock) |
| `rust/netdxf/src/collections/*.rs` | `Collections/*.cs` | Complete (TableObjects, DrawingEntities, SupportFolders) |
| `rust/netdxf/src/objects/*.rs` | `Objects/*.cs` | Complete (Group, Layout, PlotSettings, MLineStyle, ImageDefinition, UnderlayDefinition, RasterVariables, DictionaryObject, XRecord, LayerState, DxfTableStyle, DxfMLeaderStyle, DxfGeoData, DxfSun, DxfStoredSunStudy, DxfStoredField, DxfStandardFieldEvaluator) |
| `rust/netdxf/src/entities/*.rs` | `Entities/*.cs` | Complete (40 entity types including ACIS `Solid3D`/`Body`/`Region` + SAT codec, `StoredTable`, `MultiLeader`, `Light`, `Ole2Frame`, `OleFrame`, `Section`, `DimensionBlock` synthesis, advanced `Spline` knot-insertion/split/trim/degree-elevation/reverse, plus all 2D/3D/annotation/mesh primitives) |
| `rust/netdxf/src/io/*.rs` | `IO/*.cs` | Complete (DxfGroupCode, ICodeValueReader/Writer, TextCodeValueReader/Writer, BinaryCodeValueReader/Writer, DxfReader, DxfWriter for all entities & objects) |

## Previously Deferred Items — All Ported

All 5 previously deferred subsystems have been implemented and verified:
- **GTE & Advanced Spline**: `gte/` (`BSplineCurveFit`, `BSplineSurfaceFit`, `BSplineCurve`, `NURBSCurve`, `BSplineSurface`, `NURBSSurface`, `BasisFunction`, `BandedMatrix`, `GaussianElimination`, `Integration`, `RootBisection`) + `Spline::insert_knot`, `split`, `trim`, `elevate_degree`, `reverse`, `evaluate_derivative`.
- **ACIS SAT Entities & Codec**: `AcisSatChunk`, `AcisEntityData` (exact SAT substitution codec + <=255-char chunking), `Solid3D`, `Body`, `Region` with full ASCII & Binary I/O and `tests/fixtures/acis-sat` verification.
- **StoredTable, Formula Engine & Standard Fields**: `StoredTable`, `DxfTableStyle`, `DxfTableCellAddress`, `DxfTableFormula` (bounded arithmetic + `SUM`/`AVERAGE`/`COUNT`/`MIN`/`MAX` + cycle detection + overflow-safe mean), `DxfTableCalculation`, `DxfStoredField`, `DxfStandardFieldEvaluator`.
- **MultiLeader, GeoData, Sun, Light, OLE & Section**: `MultiLeader`, `DxfMLeaderStyle`, `DxfGeoData`, `DxfSun`, `DxfStoredSunStudy`, `Light`, `Ole2Frame`, `OleFrame`, `Section` with full ASCII & Binary I/O.
- **DimensionBlock Geometry Synthesis**: `DimensionBlock::build` generating anonymous `*D<n>` blocks (defpoints, extension lines, dimension lines, filled arrowheads, center marks, formatted MText) wired into `DxfDocument::build_dimension_blocks`.

## Verification & Benchmark Results

- **Lints**: `cargo clippy --all-targets -- -D warnings` — **0 warnings, 0 errors**, `unsafe_code = "forbid"`.
- **Tests**: `cargo test` — **23/23 tests passing**:
  - `math_and_color_tests` (6 tests): vectors, matrices, Arbitrary Axis OCS/WCS round-trip, Bezier curves, ACI/TrueColor/Transparency/Lineweight, units & Julian date.
  - `document_roundtrip_tests` (3 tests): full rich document round-trip in ASCII (R2000/R2007/R2018) and Binary (R2000/R2018), entity affine transforms.
  - `edge_cases_and_conformance_tests` (5 tests): table name validation & reserved protection, NURBS spline vs cubic Bezier equivalence, Polyline2D reverse, all 15 TextAlignment variants, corrupt/truncated input error handling.
  - `fixture_conformance_tests` (3 tests): `TestDxfDocument/sample.dxf` (load + ASCII & Binary round-trip), `TestDxfDocument/sample binary.dxf`, and full scan of all `.dxf` fixtures under `tests/fixtures/`.
  - `advanced_subsystems_tests` (6 tests): GTE linear algebra/integration/roots, B-spline curve & surface fitting, NURBS knot-insertion/split/trim/degree-elevation/reverse, ACIS SAT codec + `acis-sat` fixtures, table formulas/calculation/fields, and `MultiLeader`/`GeoData`/`Sun`/`SunStudy`/`Light`/`Ole2Frame`/`OleFrame`/`Section`/`DimensionBlock` ASCII & Binary round-trips.
- **Release Benchmark** (`cargo run --release --example benchmark`, 20,000 entities):
  - Build: **10.67 ms**
  - Write ASCII (2.60 MB): **12.92 ms** (~201 MB/s)
  - Read ASCII (2.60 MB): **32.53 ms** (~80 MB/s)
  - Write Binary (2.15 MB): **3.19 ms** (~674 MB/s)
  - Read Binary (2.15 MB): **12.73 ms** (~169 MB/s)


## Phase 3 — Upstream Conformance Test Parity & Cross-Interop Validation (Completed)

Ported upstream C# `tests/netDxf.Conformance/*.cs` and `tests/shared/*.cs` test suites for all ported Rust subsystems, fixing every behavioral and wire-format parity gap uncovered:

### Conformance Test Suites Organized 1:1 on Disk (`rust/netdxf/tests/`)
Mirrors upstream `tests/netDxf.Conformance/*.cs` and `tests/shared/*.cs` using the same `snake_case` 1:1 file-system rule as `rust/netdxf/src/`:
- **Entrypoint**: `rust/netdxf/tests/conformance.rs` (36 tests in a single fast Cargo test binary)
- **`rust/netdxf/tests/netdxf_conformance/`** (← `tests/netDxf.Conformance/`):
  - Math, Color, Transparency, XData: `arbitrary_axis_review_tests.rs`, `g_vector_extremes_tests.rs`, `matrix_identity_review_tests.rs`, `vector4_component_review_tests.rs`, `true_color_wire_review_tests.rs`, `transparency_stored_tests.rs`, `x_data_clone_tests.rs`
  - Header, Tables, Objects: `header_comment_tests.rs`, `header_probe_tests.rs`, `custom_header_unicode_tests.rs`, `ucs_base_tests.rs`, `geo_data_tests.rs`, `sun_tests.rs`
  - Entities & Splines: `arc_angle_fidelity_tests.rs`, `face3d_edge_visibility_tests.rs`, `m_text_unicode_chunk_tests.rs`, `entity_affine_tests.rs`, `hatch_and_dimension_tests.rs`, `spline_knot_insertion_tests.rs`, `spline_split_tests.rs`, `spline_trim_tests.rs`, `spline_degree_elevation_tests.rs`, `spline_reversal_tests.rs`
  - ACIS SAT, Tables, Fields: `acis_sat_tests.rs`, `table_calculation_tests.rs`, `standard_field_evaluator_tests.rs`
  - ASCII & Binary Codecs: `binary_sentinel_tests.rs`, `binary_chunk_tests.rs`, `strict_binary_value_tests.rs`, `strict_text_value_tests.rs`, `text_hex_tests.rs`
  - Shared helpers: `helpers.rs`
- **`rust/netdxf/tests/shared/`** (← `tests/shared/`):
  - `portable_double_cases.rs` (← `PortableDoubleCases.cs`)
  - `table_average_cases.rs` (← `TableAverageCases.cs`)
- **`rust/netdxf/examples/interop_check.rs`**: Bidirectional `.NET` (`netDxf` C#) ↔ Rust (`netdxf`) ASCII & Binary DXF interop driver.

### Parity & Wire-Interop Fixes Uncovered by Conformance Tests
1. **Binary DXF group codes `280..=289`**: Fixed `BinaryCodeValueReader` and `BinaryCodeValueWriter` to use 2-byte `i16` (matching AutoCAD R2000+ Binary DXF and C# `netDxf`), resolving stream alignment on real AutoCAD binary fixtures (`sample binary.dxf`).
2. **Binary DXF comments (`999`)**: `DxfWriter` now suppresses group code `999` comments when writing Binary DXF (required by AutoCAD and C# `BinaryCodeValueReader`).
3. **R2000 TrueColor fallback**: `DxfWriter` now suppresses group code `420` when saving `DxfVersion::AutoCad2000` (group `420` is R2004+).
4. **Object handles & `MLEADERSTYLE` references**: `DxfDocument::assign_all_handles` now assigns handles to `table_styles`, `mleader_styles`, `raster_variables`, `geo_data`, `sun`, and `sun_studies`; `MLEADERSTYLE` emits `330` owner and `342` `TextStyle` handle so C# `DxfReader` validates cleanly.
5. **Strict ASCII/Binary codec validation**:
   - `TextCodeValueReader` preserves leading/trailing spaces in ordinary strings, trims ASCII-only whitespace (`' '`, `'\t'`) for numerics/handles/booleans, strictly rejects `i16` overflow (`32768`, `-32769`), non-`0`/`1` booleans, invalid handles, non-finite doubles, and NUL bytes.
   - `BinaryCodeValueReader` strictly rejects unterminated strings at EOF, boolean bytes `2..=255`, invalid handles, and non-finite doubles.
6. **Version probe (`DxfReader::check_version`)**: Strict section-name (`0 SECTION` → `2 HEADER`) and `9 $ACADVER` → `1 <ver>` matching, skips `999` comments, always restores caller stream position.
7. **Custom header `Vector3` & `GEODATA` chunking**: `DxfReader` reconstructs custom `10`/`20`/`30` header vectors; `DxfWriter`/`DxfReader` chunk `GEODATA` `coordinate_system_definition` into `<= 255`-char `303` + `301` tags and roundtrip `305`/`306`/`307` observation tags.
8. **Unicode `MText` chunking**: `DxfWriter` splits `MText` into group `3` + final group `1` respecting both the 250 UTF-8 byte budget and 250-char budget without splitting multi-byte codepoints.
9. **Math & Spline edge cases**: Added `MathHelper::try_arbitrary_axis`, `Matrix3::is_identity_exact`, full `Matrix4` (`determinant`, `inverse`, `is_identity_exact`), `Vector4::distance`/`square_distance`, `GVector::length_robust`/`normalize`/`compute_extremes`/`orthonormalize`, `AciColor::to_packed_true_color`, `Spline` NaN guards and boundary-inclusive `trim`.
10. **Table formula precision & syntax**: Added Neumaier compensated summation for `SUM`/`AVERAGE` (`SUM(1e16, 1, -1e16) == 1.0`, overflow-safe `AVERAGE(MAX, MAX)`), `$A$1` absolute refs, leading-zero row rejection (`A01`), unary recursion depth budget, and empty-argument rejection (`=SUM()`).

### Verification Summary
- `#![forbid(unsafe_code)]` enforced at crate root (`rust/netdxf/src/lib.rs`)
- `cargo clippy --manifest-path rust/Cargo.toml --all-targets -- -D warnings`: **0 warnings, 0 errors**
- `cargo test --manifest-path rust/Cargo.toml`: **65 / 65 integration tests passed** (42 in `conformance.rs` + 23 across core/fixture/advanced suites, including `sample ascii.dxf`, `sample binary.dxf`, and all upstream conformance DXF fixtures)
- **Bidirectional `.NET` ↔ Rust interop**: Verified C# `netDxf` loads Rust-generated R2018 & R2010 ASCII + Binary DXF files (with Lines, Circles, Arcs, Unicode MText, Polylines, Splines, Blocks/Inserts, TrueColor Layers, XData binary chunks, and ACIS SAT `Solid3D`), and Rust `netdxf` loads C#-generated R2018 ASCII + Binary DXF files with exact fidelity.

## Phase 4 — Full Scope Review & Remaining Subsystems Ported (Completed)

Completed a full file-by-file audit against upstream `netDxf/` and ported all remaining previously deferred subsystems and their conformance tests:
- **Raw Tag-Level DXF & Atomic Save (`io/`)**: `DxfTag`, `DxfRawRecord`, `DxfRawSection`, `DxfRawDocument` (lossless ASCII/Binary tag-level read/edit/write with immutable `with_record`/`without_record`), and `DxfAtomicFile` (crash-safe temp-file + fsync + atomic rename wired into `DxfDocument::save_with_options` and `DxfRawDocument::save`).
- **AutoCAD R12 Codec (`io/dxf_r12_codec.rs`)**: `DxfR12Codec` (`AC1009` ASCII export and import including legacy `POLYLINE`/`VERTEX`/`SEQEND`, `LINE`, `CIRCLE`, `ARC`, `POINT`, `TEXT`, `3DFACE`, `INSERT`).
- **Value, Angular & DateTime Formatters (`units/`)**: `DxfValueFormat` (`%lu1..5`, `%pr0..8`, `%ps`, `%ct8`, `%ds`, `%th`, `%zs`), `DxfAngularValueFormat` (`%au0..4` with ties-to-even rounding), `DxfDateTimeFormat` + `CivilDateTime` (Julian calendar & mask tokens).
- **Remaining Database Objects & Preflight (`objects/` & root)**: `DxfCellStyleResolver` / `DxfCellStyleFormat`, `DxfStoredDimAssoc` / `DimAssocOsnapPointRef`, `DxfDataTable` / `DxfDataTableColumn` / `DxfDataTableCell`, `DxfLayerIndex`, `DxfLayerFilter`, `DxfSectionSettings`, `DxfStoredSectionManager`, `DxfOutputSettings`, `DxfOpaqueObject`, and `DxfVersionCompatibilityReport` (`DxfDocument::check_version_compatibility`).
- **Hardening**: Enforced `#![forbid(unsafe_code)]` in `lib.rs` and removed all panicking `.unwrap()` calls from `DxfReader` and `Spline::elevate_degree`.
- **Tests Added (`rust/netdxf/tests/netdxf_conformance/`)**: `raw_document_tests.rs`, `r12_codec_tests.rs`, `value_format_tests.rs`, `cell_style_and_objects_tests.rs`.


## Automated API Parity Validation & Full Public Surface Completion

- **Automated Parity Tool**: Added [`tools/rust_port/compare_api.py`](../../tools/rust_port/compare_api.py) to automatically scan all public C# types in `netDxf/` against all public Rust types in `rust/netdxf/src/`, generate [`doc/rust-port/API-PARITY-REPORT.md`](./API-PARITY-REPORT.md), and enforce zero missing public types via `--fail-on-missing`.
- **Ported Remaining Public Types (180+ types across all namespaces)**:
  - **Units**: `UnitStyleFormat`, `LinearUnitFormat` (exact binary64 ties-to-even rounding via internal `BigUint`), `AngleUnitFormat` (DMS, Gradians, Radians, Surveyor bearings).
  - **Tables**: `DimensionStyleOverride`, `DimensionStyleOverrideType`, `LinetypeSegmentType`, `TextStyleFontData`, `UcsFlags`, `UcsOrthographicType`, `VPortFlags`, `ViewFlags`, `ViewModeFlags`, `ViewRenderMode`, `ViewUcs`.
  - **Root & Blocks**: `DxfDimensionPictureOptions`, `DxfVersionCompatibilityDiagnostic`, `DxfVersionCompatibilityKind`, `AttributeDefinitionRenamePlan`.
  - **GTE**: `GTE`, `LexicoArray2`, `ParametricCurve`, `ParametricSurface`, `TIQueryIntervals`, `FIQueryIntervals`, `FIQueryIntervalsType`, `RootsBisection`, `RootsPolynomial`, `BSplineReduction`.
  - **Entities**: `DimensionArrowhead`, `ToleranceEntry`, `ToleranceValue`, `ToleranceGeometricSymbol`, `ToleranceMaterialCondition`, `DatumReferenceValue`, `MTextBackgroundFill`, `MTextBackgroundFillFlags`, `MTextColumns`, `MTextColumnType`, `MTextColumnStorage`, `MTextFormattingOptions`, `MTextParagraphOptions`, `MTextParagraphAlignment`, `MTextParagraphVerticalAlignment`, `EntityShadowMode`, `HelixConstraint`, `LightAttenuationType`, `LightShadowType`, `OleObjectType`, `Ole2FrameMetadataFields`, `SplineParameterSide`, `Edge`, `EdgeType`, `Polyline`, `AttributeMText`, `AttributeTextData`, `AttributeDefinitionBindingChange`, `InsertAttributeReplacement`, `PolyfaceMeshRecord`, `PolygonMeshRecord`, `Polyline2DRecord`, `Polyline3DRecord`, `StoredTableGrid`, and all 12 structured `MLeader*` context/data types.
  - **Objects**: `DictionaryCloningFlags`, `LayerPropertiesFlags`, `LayerPropertiesRestoreFlags`, `PlotFlags`, `PlotType`, `ShadePlotMode`, `ShadePlotResolutionMode`, `SupportedImageFormats`, `DxfDictionary`, `DxfDictionaryEntry`, `DxfDictionaryWithDefault`, `DxfDictionaryVariable`, `DxfIdBuffer`, `DxfLightList`, `DxfLightListEntry`, `DxfPlaceholder`, `DxfPlotSettingsObject`, `DxfSortentsTable`, `DxfSortOrderEntry`, `DxfSpatialFilter`, `DxfSpatialIndex`, `DxfVbaProject`, `DxfWipeoutVariables`, `DxfXRecord`, plus Geo/Sun/Section/DataTable/CellStyle/Field/TableStyle/StoredTableContent sub-types.
  - **IO**: `DxfDuplicateRecordCloning`, raw handle model (`DxfRawHandleRole`, `DxfRawHandleDiagnosticKind`, `DxfRawHandleDiagnostic`, `DxfRawHandleOccurrence`, `DxfRawHandleIndexOptions`, `DxfRawReferenceTraversal`, `DxfRawDependencyClosure`), raw geometry views (`DxfRaw*Geometry`), and raw object store (`DxfRawObjectStore`, `DxfRawObjectStoreOptions`, `DxfRawObjectTransaction`, `DxfRawStoredObject`, `DxfRawDictionary`, `DxfRawXRecord`, etc.).
- **Verification**:
  - `python3 tools/rust_port/compare_api.py --report doc/rust-port/API-PARITY-REPORT.md --fail-on-missing` → **100.0% API coverage** (`464` C# types scanned, `387` direct 1:1 matches, `63` idiomatic Rust mappings, `14` C# EventArgs/Delegates N/A, **`0` missing**).
  - `cargo clippy --manifest-path rust/Cargo.toml --all-targets -- -D warnings` → **0 warnings** (`#![forbid(unsafe_code)]`).
  - `cargo test --manifest-path rust/Cargo.toml` → **71/71 passing** (including `api_parity_ported_tests.rs`).
