use netdxf::entities::{AcisEntityData, Body, EntityObject, Region, Solid3D};
use netdxf::DxfDocument;

use super::helpers::SAT_VERSIONS;

#[test]
fn acis_sat_codec_printable_chunking_and_validation() {
    let printable: String = (32u8..=126).map(|c| c as char).collect();
    let long_line = format!("{}{}{}", "0".repeat(254), "A", "C".repeat(260));
    let lines = vec![
        printable,
        "  leading and trailing  ".to_string(),
        String::new(),
        long_line,
        "700 0 1 0".to_string(),
    ];

    let mut data = AcisEntityData::new();
    data.set_sat_lines(&lines).expect("encode valid SAT lines");
    assert!(
        data.encoded_sat_chunks().iter().any(|c| c.group_code == 3),
        "long SAT line should be chunked with group 3"
    );
    assert!(
        data.encoded_sat_chunks().iter().all(|c| c.text.len() <= 255),
        "SAT chunk must not exceed 255 chars"
    );
    assert_eq!(data.sat_lines(), lines.as_slice());

    assert!(data.set_sat_lines(["valid", "invalid\nline"]).is_err());
    assert!(data.set_sat_lines(["valid", "caf\u{00e9}"]).is_err());
    assert!(data.set_sat_lines(["\0"]).is_err());
    assert!(data.set_sat_lines(["\x7f"]).is_err());
    assert_eq!(data.sat_lines(), lines.as_slice());
}

#[test]
fn acis_sat_entities_wire_roundtrip_all_sat_versions() {
    let sat_lines = vec![
        "700 0 1 0".to_string(),
        "@7 netDxf @7 RustSAT @24 Mon Nov 23 12:00:00 2026".to_string(),
        "body $-1 -1 $-1 $1 $-1 $-1 #".to_string(),
        "End-of-ACIS-data".to_string(),
    ];

    for &version in &SAT_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);

            let mut solid = Solid3D::new();
            solid.acis.set_sat_lines(&sat_lines).unwrap();
            let mut body = Body::new();
            body.acis.set_sat_lines(&sat_lines).unwrap();
            let mut region = Region::new();
            region.acis.set_sat_lines(&sat_lines).unwrap();

            doc.add_entity(solid);
            doc.add_entity(body);
            doc.add_entity(region);

            let bytes = doc.save_to_bytes(binary).expect("save ACIS entities");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load ACIS entities");

            let loaded_solid = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Solid3D(s) => Some(s),
                    _ => None,
                })
                .expect("Solid3D");
            let loaded_body = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Body(b) => Some(b),
                    _ => None,
                })
                .expect("Body");
            let loaded_region = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Region(r) => Some(r),
                    _ => None,
                })
                .expect("Region");

            assert_eq!(loaded_solid.acis.sat_lines(), sat_lines.as_slice());
            assert_eq!(loaded_body.acis.sat_lines(), sat_lines.as_slice());
            assert_eq!(loaded_region.acis.sat_lines(), sat_lines.as_slice());
        }
    }
}
