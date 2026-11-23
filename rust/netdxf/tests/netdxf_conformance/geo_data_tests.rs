use netdxf::header::DxfVersion;
use netdxf::objects::DxfGeoData;
use netdxf::{DxfDocument, Vector2, Vector3};

#[test]
fn geodata_chunked_definition_roundtrip() {
    let long_def = format!("{}{}{}", "x".repeat(252), "Zolc_UTF8_", "z".repeat(600));
    for &version in &[
        DxfVersion::AutoCad2010,
        DxfVersion::AutoCad2013,
        DxfVersion::AutoCad2018,
    ] {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let geo = DxfGeoData {
                design_coordinates_type: 1,
                design_point: Vector3::new(1.25, -2.5, 7.75),
                reference_point: Vector3::new(11.5, 48.25, 100.125),
                horizontal_unit_scale: 0.3048,
                vertical_unit_scale: 0.0254,
                horizontal_units: 2,
                vertical_units: 1,
                north_direction: Vector2::new(3.0, 4.0),
                up_direction: Vector3::new(2.0, 3.0, 4.0),
                scale_estimation_method: 4,
                user_scale_factor: 0.9996,
                coordinate_system_definition: long_def.clone(),
                geo_rss_tag: "metadata".into(),
                observation_from_tag: "origin Żółć".into(),
                observation_to_tag: "target 東京".into(),
                observation_coverage_tag: "survey bounds".into(),
                ..Default::default()
            };
            doc.geo_data = Some(geo);

            let bytes = doc.save_to_bytes(binary).expect("save GEODATA");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load GEODATA");
            let d = loaded.geo_data.as_ref().expect("loaded GEODATA");
            assert_eq!(d.coordinate_system_definition, long_def);
            assert_eq!(d.design_point, Vector3::new(1.25, -2.5, 7.75));
            assert_eq!(d.reference_point, Vector3::new(11.5, 48.25, 100.125));
            assert_eq!(d.north_direction, Vector2::new(3.0, 4.0));
            assert_eq!(d.up_direction, Vector3::new(2.0, 3.0, 4.0));
            assert_eq!(d.horizontal_units, 2);
            assert_eq!(d.vertical_units, 1);
            assert_eq!(d.observation_to_tag, "target 東京");
        }
    }
}
