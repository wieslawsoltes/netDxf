use netdxf::entities::{Line, Solid3D};
use netdxf::header::DxfVersion;
use netdxf::io::DxfTag;
use netdxf::objects::{
    DimAssocOsnapPointRef, DxfCellStyleFormat, DxfCellStyleResolver, DxfDataTable,
    DxfDataTableCell, DxfDataTableColumn, DxfGeoData, DxfLayerFilter, DxfLayerIndex,
    DxfOpaqueObject, DxfOutputSettings, DxfSectionSettings, DxfStoredDimAssoc,
    DxfStoredSectionManager, DxfSun,
};
use netdxf::{AciColor, DxfDocument, Vector3};

#[test]
fn cell_style_resolver_and_auxiliary_objects_conformance() {
    let table_style = DxfCellStyleFormat {
        text_style: Some("Standard".into()),
        text_height: Some(2.5),
        text_color: Some(AciColor::DEFAULT),
        horizontal_margin: Some(1.5),
        ..Default::default()
    };
    let col = DxfCellStyleFormat {
        text_color: Some(AciColor::BLUE),
        ..Default::default()
    };
    let row = DxfCellStyleFormat {
        text_height: Some(3.0),
        ..Default::default()
    };
    let cell = DxfCellStyleFormat {
        text_color: Some(AciColor::RED),
        format_string: Some("%lu2%pr2".into()),
        ..Default::default()
    };

    let resolved = DxfCellStyleResolver::resolve(&table_style, Some(&col), Some(&row), Some(&cell));
    assert_eq!(resolved.text_style.as_deref(), Some("Standard"));
    assert_eq!(resolved.text_height, Some(3.0));
    assert_eq!(resolved.text_color, Some(AciColor::RED));
    assert_eq!(resolved.horizontal_margin, Some(1.5));
    assert_eq!(resolved.format_string.as_deref(), Some("%lu2%pr2"));

    // Auxiliary database objects construct & preserve fields cleanly
    let dim_assoc = DxfStoredDimAssoc {
        dimension_handle: Some("1A2".into()),
        associativity_flags: 1,
        point_refs: vec![DimAssocOsnapPointRef {
            osnap_type: 1,
            osnap_point: Vector3::new(1.0, 2.0, 3.0),
            ..Default::default()
        }],
        ..Default::default()
    };
    assert_eq!(dim_assoc.point_refs.len(), 1);

    let data_table = DxfDataTable {
        name: "Materials".into(),
        row_count: 1,
        columns: vec![DxfDataTableColumn {
            name: "Density".into(),
            column_type: 2,
            cells: vec![DxfDataTableCell::Double(7850.0)],
        }],
        ..Default::default()
    };
    assert_eq!(data_table.columns[0].cells[0], DxfDataTableCell::Double(7850.0));

    let layer_idx = DxfLayerIndex {
        entries: vec![("0".into(), 5)],
        ..Default::default()
    };
    let layer_filter = DxfLayerFilter {
        layer_names: vec!["0".into(), "Geometry".into()],
        ..Default::default()
    };
    assert_eq!(layer_idx.entries.len(), 1);
    assert_eq!(layer_filter.layer_names.len(), 2);

    let sec_settings = DxfSectionSettings::default();
    let sec_mgr = DxfStoredSectionManager::default();
    let out_settings = DxfOutputSettings::default();
    let opaque = DxfOpaqueObject::new("CUSTOM_OBJ", vec![DxfTag::string(1, "payload")]);
    assert_eq!(sec_settings.current_section_type, 1);
    assert!(!sec_mgr.requires_full_update);
    assert!(!out_settings.scale_to_fit);
    assert_eq!(opaque.tags.len(), 1);
}

#[test]
fn version_compatibility_report_conformance() {
    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    let mut line = Line::new(Vector3::ZERO, Vector3::UNIT_X);
    line.common.color = AciColor::from_rgb(10, 20, 30);
    doc.add_entity(line);
    doc.sun = Some(DxfSun::default());
    doc.geo_data = Some(DxfGeoData::default());

    let r2000 = doc.check_version_compatibility(DxfVersion::AutoCad2000);
    assert!(!r2000.is_compatible());
    assert!(r2000.issues.iter().any(|i| i.feature == "TrueColor"));
    assert!(r2000.issues.iter().any(|i| i.feature == "Sun"));
    assert!(r2000.issues.iter().any(|i| i.feature == "GeoData"));

    let r2018 = doc.check_version_compatibility(DxfVersion::AutoCad2018);
    assert!(r2018.is_compatible());

    let mut solid = Solid3D::new();
    solid.acis.set_sat_lines(["700 0 1 0"]).unwrap();
    doc.add_entity(solid);
    let r2013 = doc.check_version_compatibility(DxfVersion::AutoCad2013);
    assert!(r2013.issues.iter().any(|i| i.feature == "AcisSat"));
}
