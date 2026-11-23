use netdxf::blocks::{AttributeDefinitionRenamePlan, Block};
use netdxf::entities::{
    AttributeDefinition, DatumReferenceValue, DimensionArrowhead, EntityShadowMode,
    HelixConstraint, LightAttenuationType, LightShadowType, MLeaderContext, MLeaderData,
    MTextBackgroundFill, MTextBackgroundFillFlags, MTextColumnStorage, MTextColumnType,
    MTextColumns, MTextFormattingOptions, MTextParagraphAlignment, MTextParagraphOptions,
    MTextParagraphVerticalAlignment, Ole2FrameMetadataFields, OleObjectType, Polyline2D,
    Polyline2DRecord, Polyline2DVertex, SplineParameterSide, StoredTableGrid, ToleranceEntry,
    ToleranceGeometricSymbol, ToleranceMaterialCondition, ToleranceValue,
};
use netdxf::gte::{
    BSplineReduction, FIQueryIntervals, FIQueryIntervalsType, LexicoArray2, RootsPolynomial,
    TIQueryIntervals,
};
use netdxf::header::DxfVersion;
use netdxf::io::{
    DxfDuplicateRecordCloning, DxfRawDictionary, DxfRawDictionaryEntry, DxfRawLineGeometry,
    DxfRawObjectStore, DxfRawObjectStoreOptions, DxfRawPlaceholder, DxfRawReferenceTraversal,
    DxfRawStoredObject,
};
use netdxf::objects::{
    DictionaryCloningFlags, DxfCellProperty, DxfDataCellType, DxfDictionary, DxfDictionaryEntry,
    DxfDictionaryVariable, DxfDictionaryWithDefault, DxfFieldResultStatus, DxfGeoCoordinateType,
    DxfIdBuffer, DxfLightList, DxfLightListEntry, DxfPlaceholder, DxfPlotSettingsObject,
    DxfSortOrderEntry, DxfSortentsTable, DxfSpatialFilter, DxfSpatialIndex, DxfSunShadowType,
    DxfVbaProject, DxfWipeoutVariables, LayerPropertiesFlags, LayerPropertiesRestoreFlags,
    PlotFlags, PlotType, ShadePlotMode, ShadePlotResolutionMode, SupportedImageFormats,
};
use netdxf::tables::{
    DimensionStyleOverride, DimensionStyleOverrideType, DimensionStyleOverrideValue, FontStyle,
    LinetypeSegmentType, TextStyleFontData, UcsFlags, UcsOrthographicType, VPortFlags, ViewFlags,
    ViewModeFlags, ViewRenderMode, ViewUcs,
};
use netdxf::units::{
    AngleUnitFormat, AngleUnitType, FractionFormatType, LinearUnitFormat, UnitStyleFormat,
};
use netdxf::{
    AciColor, DxfDimensionPictureOptions, DxfDocument, DxfVersionCompatibilityKind,
    DxfVersionCompatibilityReport, Vector3,
};

#[test]
fn linear_and_angle_unit_formats_round_and_carry_accurately() {
    let mut fmt = UnitStyleFormat::new();
    fmt.linear_decimal_places = 2;
    // Exact binary64 midpoints tie to even (0.125 -> 0.12, 0.375 -> 0.38)
    assert_eq!(LinearUnitFormat::to_decimal(0.125, &fmt).unwrap(), "0.12");
    assert_eq!(LinearUnitFormat::to_decimal(0.375, &fmt).unwrap(), "0.38");
    // 12.345_f64 is slightly above 12.345 in binary64, so exact rounding yields 12.35
    assert_eq!(LinearUnitFormat::to_decimal(12.345, &fmt).unwrap(), "12.35");
    assert_eq!(
        LinearUnitFormat::to_engineering(11.999, &fmt).unwrap(),
        "1'"
    );
    fmt.suppress_zero_inches = false;
    assert_eq!(
        LinearUnitFormat::to_engineering(11.999, &fmt).unwrap(),
        "1'-0.00\""
    );

    let mut frac_fmt = UnitStyleFormat::new();
    frac_fmt.linear_decimal_places = 4;
    frac_fmt.fraction_type = FractionFormatType::NotStacked;
    assert_eq!(
        LinearUnitFormat::to_fractional(2.5, &frac_fmt).unwrap(),
        "2 1/2"
    );
    assert_eq!(
        LinearUnitFormat::to_architectural(18.5, &frac_fmt).unwrap(),
        "1'-6 1/2\""
    );
    assert!(LinearUnitFormat::to_decimal(f64::NAN, &fmt).is_err());

    let mut ang_fmt = UnitStyleFormat::new();
    ang_fmt.angular_decimal_places = 4;
    assert_eq!(
        AngleUnitFormat::to_degrees_minutes_seconds(45.5, &ang_fmt).unwrap(),
        "45°30'0\""
    );
    assert_eq!(
        AngleUnitFormat::to_surveyor(45.0, &ang_fmt, true).unwrap(),
        "N 45°0'0\" E"
    );
    assert_eq!(
        AngleUnitFormat::format(180.0, AngleUnitType::Gradians, &ang_fmt, false).unwrap(),
        "200.0000g"
    );
}

#[test]
fn tables_overrides_font_data_and_view_ucs_validate() {
    let ok = DimensionStyleOverride::new(
        DimensionStyleOverrideType::ArrowSize,
        DimensionStyleOverrideValue::Float64(2.5),
    );
    assert!(ok.is_ok());

    let bad = DimensionStyleOverride::new(
        DimensionStyleOverrideType::ArrowSize,
        DimensionStyleOverrideValue::Float64(-1.0),
    );
    assert!(bad.is_err());

    let font = TextStyleFontData::new("Arial", 0x0300_0000).unwrap();
    assert_eq!(font.font_style(), FontStyle::BOLD | FontStyle::ITALIC);
    assert!(TextStyleFontData::new("a".repeat(256), 0).is_err());

    let mut ucs = ViewUcs::new();
    assert!(ucs.validate().is_ok());
    ucs.base_ucs = Some("CUSTOM".into());
    assert!(ucs.validate().is_err());
    ucs.orthographic_type = UcsOrthographicType::Top as i16;
    assert!(ucs.validate().is_ok());

    assert_eq!(LinetypeSegmentType::Simple, LinetypeSegmentType::Simple);
    assert!(UcsFlags::REFERENCED.contains(UcsFlags::REFERENCED));
    assert!(VPortFlags::EXTERNALLY_DEPENDENT.bits() == 16);
    assert!(ViewFlags::PAPER_SPACE.bits() == 1);
    assert!(ViewModeFlags::PERSPECTIVE.bits() == 1);
    assert_eq!(ViewRenderMode::from_i16(3), Some(ViewRenderMode::FlatShaded));
}

#[test]
fn attribute_definition_rename_plan_swaps_and_enforces_single_use() {
    let mut block = Block::new("TEST_BLK").unwrap();
    block
        .attribute_definitions
        .insert("A".into(), AttributeDefinition::new("A"));
    block
        .attribute_definitions
        .insert("B".into(), AttributeDefinition::new("B"));

    let mut plan = AttributeDefinitionRenamePlan::prepare(
        &block,
        &[
            ("A".to_string(), "B".to_string()),
            ("B".to_string(), "A".to_string()),
        ],
    )
    .unwrap();
    plan.apply(&mut block).unwrap();
    let tags: Vec<&str> = block
        .attribute_definitions
        .values()
        .map(|d| d.tag.as_str())
        .collect();
    assert_eq!(tags, vec!["B", "A"]);
    // Single-use enforcement
    assert!(plan.apply(&mut block).is_err());
}

#[test]
fn dimension_picture_options_and_version_compatibility_diagnostics() {
    assert!(DxfDimensionPictureOptions::new(0, 10).is_err());
    let opts = DxfDimensionPictureOptions::default();
    assert_eq!(opts.maximum_pictures, 10_000);

    let mut doc = DxfDocument::new();
    doc.layers
        .get_mut("0")
        .unwrap()
        .color = AciColor::from_rgb(10, 20, 30);
    let report = DxfVersionCompatibilityReport::analyze(&doc, DxfVersion::AutoCad2000);
    assert!(!report.is_compatible());
    assert!(report.has_known_losses);
    assert!(!report.has_known_rejections);
    assert_eq!(
        report.diagnostics[0].kind,
        DxfVersionCompatibilityKind::DataOmission
    );
}

#[test]
fn gte_intervals_lexico_polynomial_and_bspline_reduction() {
    let mut arr = LexicoArray2::new(2, 3, vec![1.0, 2.0, 3.0, 4.0, 5.0, 6.0]);
    assert_eq!(arr.get(1, 2), 6.0);
    arr.set(0, 1, 9.0);
    assert_eq!(arr.get(0, 1), 9.0);

    let ti = TIQueryIntervals::dynamic(10.0, [0.0, 1.0], 1.0, [5.0, 6.0], -1.0);
    assert!(ti.intersect);
    assert!((ti.first_time - 2.0).abs() < 1e-12);

    let fi = FIQueryIntervals::finite_finite([0.0, 4.0], [2.0, 6.0]);
    assert_eq!(fi.query_type, FIQueryIntervalsType::IsFinite);
    assert_eq!(fi.overlap, [2.0, 4.0]);

    // (x - 1)(x - 2)(x - 3) = -6 + 11x - 6x^2 + x^3
    let roots = RootsPolynomial::find(&[-6.0, 11.0, -6.0, 1.0], 128);
    assert_eq!(roots.len(), 3);
    assert!((roots[0] - 1.0).abs() < 1e-7);
    assert!((roots[1] - 2.0).abs() < 1e-7);
    assert!((roots[2] - 3.0).abs() < 1e-7);

    let controls: Vec<Vector3> = (0..10)
        .map(|i| Vector3::new(i as f64, (i as f64 * 0.3).sin(), 0.0))
        .collect();
    let reduced = BSplineReduction::new(&controls, 3, 0.5);
    assert_eq!(reduced.degree(), 3);
    assert_eq!(reduced.control_data().len(), 5);
}

#[test]
fn entities_objects_and_raw_io_ported_types_work() {
    let dot = DimensionArrowhead::dot();
    assert_eq!(dot.table.name, "_DOT");
    assert_eq!(dot.entities.len(), 2);

    let mut entry = ToleranceEntry::new();
    entry.geometric_symbol = ToleranceGeometricSymbol::Position;
    entry.tolerance1 = Some(ToleranceValue::new(
        true,
        "0.05",
        ToleranceMaterialCondition::Maximum,
    ));
    entry.datum1 = Some(DatumReferenceValue::new("A", ToleranceMaterialCondition::None));
    assert_eq!(entry.geometric_symbol, ToleranceGeometricSymbol::Position);

    let fill = MTextBackgroundFill {
        flags: MTextBackgroundFillFlags::USE_COLOR | MTextBackgroundFillFlags::TEXT_FRAME,
        ..Default::default()
    };
    assert!(fill.flags.contains(MTextBackgroundFillFlags::TEXT_FRAME));

    let cols = MTextColumns {
        column_type: MTextColumnType::Dynamic,
        storage: MTextColumnStorage::Embedded,
        ..Default::default()
    };
    assert_eq!(cols.column_type, MTextColumnType::Dynamic);

    let _fmt = MTextFormattingOptions::default();
    let para = MTextParagraphOptions {
        alignment: MTextParagraphAlignment::Justified,
        vertical_alignment: MTextParagraphVerticalAlignment::Top,
        ..Default::default()
    };
    assert_eq!(para.alignment, MTextParagraphAlignment::Justified);

    let _ = (
        EntityShadowMode::Cast,
        HelixConstraint::Height,
        LightAttenuationType::InverseSquare,
        LightShadowType::ShadowMap,
        OleObjectType::Embedded,
        Ole2FrameMetadataFields::ALL,
        SplineParameterSide::Left,
    );

    let poly = Polyline2D::new(vec![Polyline2DVertex::new(0.0, 0.0)], false);
    let rec = Polyline2DRecord::from(&poly);
    assert_eq!(rec.vertexes.len(), 1);
    let grid = StoredTableGrid::new(vec![10.0, 10.0], vec![20.0]);
    assert_eq!((grid.row_count(), grid.column_count()), (2, 1));
    let mleader = MLeaderData {
        context: MLeaderContext::default(),
        ..Default::default()
    };
    assert_eq!(mleader.context.content_scale, 1.0);

    // Objects
    let mut dict = DxfDictionary::new();
    dict.cloning_flags = DictionaryCloningFlags::UseClone;
    dict.entries.push(DxfDictionaryEntry {
        name: "K".into(),
        target_handle: "100".into(),
    });
    let _ = (
        DxfDictionaryWithDefault::new(),
        DxfDictionaryVariable::new("val"),
        DxfIdBuffer::new(),
        DxfLightList {
            entries: vec![DxfLightListEntry {
                light_handle: "200".into(),
            }],
            ..DxfLightList::new()
        },
        DxfPlaceholder::new(),
        DxfPlotSettingsObject::new("Setup1"),
        DxfSortentsTable {
            entries: vec![DxfSortOrderEntry {
                entity_handle: "A".into(),
                sort_handle: "B".into(),
            }],
            ..DxfSortentsTable::new()
        },
        DxfSpatialFilter::new(),
        DxfSpatialIndex::new(),
        DxfVbaProject::new(),
        DxfWipeoutVariables::new(),
        LayerPropertiesFlags::FROZEN,
        LayerPropertiesRestoreFlags::ALL,
        PlotFlags::PLOT_CENTERED,
        PlotType::Window,
        ShadePlotMode::Rendered,
        ShadePlotResolutionMode::Custom,
        SupportedImageFormats::Png,
        DxfCellProperty::ALL,
        DxfDataCellType::Double,
        DxfFieldResultStatus::Success,
        DxfGeoCoordinateType::ProjectedGrid,
        DxfSunShadowType::AreaSampled,
    );

    // Raw IO object store & transaction
    let mut store = DxfRawObjectStore::new(DxfRawObjectStoreOptions::default());
    let mut tx = store.begin_transaction();
    tx.upsert(DxfRawStoredObject::Dictionary(DxfRawDictionary {
        handle: 0x10,
        owner_handle: 0x0,
        is_hard_owner: false,
        cloning: DxfDuplicateRecordCloning::KeepExisting,
        entries: vec![DxfRawDictionaryEntry {
            name: "PH".into(),
            target_handle: 0x11,
            hard_owner: false,
        }],
        ..Default::default()
    }));
    tx.upsert(DxfRawStoredObject::Placeholder(DxfRawPlaceholder {
        handle: 0x11,
        owner_handle: 0x10,
    }));
    tx.commit(&mut store);
    assert!(store.get(0x10).is_some());
    assert!(store.get(0x11).is_some());
    assert!(DxfRawReferenceTraversal::ALL.contains(DxfRawReferenceTraversal::HARD_POINTERS));
    let _line_geom = DxfRawLineGeometry::default();
}
