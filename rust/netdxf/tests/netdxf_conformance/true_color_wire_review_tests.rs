use netdxf::entities::{EntityObject, Line};
use netdxf::header::DxfVersion;
use netdxf::tables::Layer;
use netdxf::{AciColor, DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn true_color_packed_and_wire_review() {
    let color = AciColor::from_rgb(18, 171, 52);
    assert_eq!(color.to_true_color(), 0x12AB34);
    assert_eq!(color.to_packed_true_color(), 0xC212_AB34_u32 as i32);

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let mut layer = Layer::new("REVIEW_RGB").unwrap();
            layer.color = color;
            doc.layers.add(layer);
            let mut line = Line::new(Vector3::ZERO, Vector3::UNIT_X);
            line.common.color = color;
            line.common.layer = "REVIEW_RGB".into();
            doc.add_entity(line);

            let bytes = doc.save_to_bytes(binary).expect("save truecolor");
            if !binary {
                let text = String::from_utf8_lossy(&bytes);
                let has_420 = text
                    .lines()
                    .collect::<Vec<_>>()
                    .windows(2)
                    .any(|w| w[0].trim() == "420" && w[1].trim() == format!("{}", 0x12AB34));
                if version == DxfVersion::AutoCad2000 {
                    assert!(!has_420, "R2000 must not emit group 420");
                } else {
                    assert!(
                        has_420,
                        "{version:?} should emit untagged RGB 0x12AB34 in group 420"
                    );
                }
            }
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load truecolor");
            let loaded_line = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) => Some(l),
                    _ => None,
                })
                .unwrap();
            if version >= DxfVersion::AutoCad2004 {
                assert!(loaded_line.common.color.use_true_color);
                assert_eq!(loaded_line.common.color.to_rgb(), [18, 171, 52]);
            }
        }
    }
}
