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
