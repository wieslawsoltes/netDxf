use netdxf::entities::{EntityObject, Line};
use netdxf::header::DxfVersion;
use netdxf::io::{DxfRawDocument, DxfTag};
use netdxf::{DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn raw_document_lossless_roundtrip_and_record_editing() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            doc.add_entity(Line::new(
                Vector3::new(1.0, 2.0, 3.0),
                Vector3::new(4.0, 5.0, 6.0),
            ));
            let bytes = doc.save_to_bytes(binary).expect("save seed");
            let raw = DxfRawDocument::load_from_bytes(&bytes).expect("load raw");
            assert_eq!(raw.is_binary, binary);

            let entities_sec = raw
                .sections
                .iter()
                .find(|s| s.name == "ENTITIES")
                .expect("ENTITIES section");
            let line_rec = entities_sec
                .records
                .iter()
                .find(|r| r.name == "LINE")
                .expect("LINE record");
            assert!(line_rec.handle().is_some());

            // Immutably edit X coordinate of LINE start point from 1.0 -> 99.5
            let edited_tags: Vec<DxfTag> = line_rec
                .tags
                .iter()
                .map(|t| {
                    if t.code == 10 {
                        DxfTag::double(10, 99.5)
                    } else {
                        t.clone()
                    }
                })
                .collect();
            let edited_raw = raw.with_record(line_rec, edited_tags);

            // Original raw snapshot must remain unchanged
            let orig_x = raw
                .sections
                .iter()
                .find(|s| s.name == "ENTITIES")
                .unwrap()
                .records
                .iter()
                .find(|r| r.name == "LINE")
                .unwrap()
                .tags
                .iter()
                .find(|t| t.code == 10)
                .unwrap()
                .value
                .as_f64();
            assert_eq!(orig_x, 1.0);

            // Save edited raw in opposite transport and load into DxfDocument
            let edited_bytes = edited_raw.save_to_bytes(!binary).expect("save edited raw");
            let reloaded = DxfDocument::load_from_bytes(&edited_bytes).expect("reload edited raw");
            let line = reloaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) => Some(l.as_ref()),
                    _ => None,
                })
                .expect("reloaded LINE");
            assert_eq!(line.start_point.x, 99.5);
            assert_eq!(line.end_point, Vector3::new(4.0, 5.0, 6.0));

            // Remove LINE record via without_record
            let removed_raw = raw.without_record(line_rec);
            let removed_bytes = removed_raw.save_to_bytes(binary).expect("save removed raw");
            let empty_doc = DxfDocument::load_from_bytes(&removed_bytes).expect("reload removed");
            assert_eq!(empty_doc.entities.lines().count(), 0);
        }
    }

    // Atomic file save roundtrip
    let tmp = std::env::temp_dir().join("netdxf_rust_atomic_save_test.dxf");
    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    doc.add_entity(Line::new(Vector3::ZERO, Vector3::UNIT_X));
    doc.save_with_options(&tmp, false).expect("atomic ASCII save");
    let loaded = DxfDocument::load(&tmp).expect("load atomic ASCII");
    assert_eq!(loaded.entities.lines().count(), 1);
    doc.save_with_options(&tmp, true).expect("atomic Binary save");
    let loaded_bin = DxfDocument::load(&tmp).expect("load atomic Binary");
    assert_eq!(loaded_bin.entities.lines().count(), 1);
    let _ = std::fs::remove_file(&tmp);
}
