use netdxf::entities::{EntityObject, Line, MText};
use netdxf::{DxfDocument, Transparency, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn transparency_stored_and_wire_conformance() {
    assert_eq!(Transparency::default().stored_alpha_value(), None);
    assert_eq!(Transparency::new(0).unwrap().stored_alpha_value(), None);
    assert_eq!(Transparency::BY_BLOCK.stored_alpha_value(), None);
    assert_eq!(Transparency::BY_LAYER.stored_alpha_value(), None);
    assert_eq!(Transparency::BY_BLOCK.to_alpha_value(), 0x0100_0000);
    assert_eq!(Transparency::new(0).unwrap().to_alpha_value(), 0x0200_00FF);

    let mut packed_samples = Vec::new();
    for mode in [0x0100_0000_i32, 0x0200_0000_i32] {
        for alpha in 0..256 {
            packed_samples.push(mode | alpha);
        }
    }
    packed_samples.extend_from_slice(&[
        0,
        -1,
        i32::MIN,
        i32::MAX,
        0x0300_007F,
        0x0012_3456,
        0xA512_3481_u32 as i32,
        0x4200_0000,
    ]);

    for packed in packed_samples {
        let mut item = Transparency::from_alpha_value(packed);
        assert_eq!(item.stored_alpha_value(), Some(packed));
        assert_eq!(item.to_alpha_value(), packed);

        let pct = (100.0 - ((packed & 255) as f64 / 255.0) * 100.0) as i16;
        let legacy = Transparency::from_cad_index(pct);
        assert_eq!(item.value(), legacy.value());
        assert_eq!(item.is_by_block(), legacy.is_by_block());
        assert_eq!(item, legacy);

        assert!(item.set_value(-1).is_err());
        assert!(item.set_value(91).is_err());
        assert_eq!(item.stored_alpha_value(), Some(packed));

        item.set_value(25).unwrap();
        assert_eq!(item.stored_alpha_value(), None);
        assert_eq!(
            item.to_alpha_value(),
            Transparency::new(25).unwrap().to_alpha_value()
        );
    }

    let state_zero = Transparency::from_layer_state_alpha_value(0);
    assert_eq!(state_zero.stored_alpha_value(), Some(0));
    assert_eq!(state_zero.value(), 0);

    let wire_values = [
        0x0200_0000_i32,
        0x0200_0001,
        0x0200_0080,
        0x0200_00FE,
        0x0200_00FF,
        0x0100_0000,
    ];
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            for (i, &packed) in wire_values.iter().enumerate() {
                let mut line = Line::new(
                    Vector3::new(i as f64, 0.0, 0.0),
                    Vector3::new(i as f64, 1.0, 0.0),
                );
                line.common.transparency = Transparency::from_alpha_value(packed);
                doc.add_entity(line);

                let mut mtext =
                    MText::new(format!("packed {i}"), Vector3::new(i as f64, 2.0, 0.0), 1.0);
                mtext.common.transparency = Transparency::from_alpha_value(packed);
                doc.add_entity(mtext);
            }
            let bytes = doc.save_to_bytes(binary).expect("save transparency wire");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load transparency wire");
            let mut loaded_line_alphas: Vec<i32> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Line(l) => l.common.transparency.stored_alpha_value(),
                    _ => None,
                })
                .collect();
            loaded_line_alphas.sort();
            let mut expected = wire_values.to_vec();
            expected.sort();
            assert_eq!(
                loaded_line_alphas, expected,
                "version={version:?}, binary={binary}"
            );
        }
    }
}
