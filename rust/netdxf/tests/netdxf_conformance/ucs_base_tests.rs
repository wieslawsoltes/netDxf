use netdxf::objects::{LayerState, LayerStateProperties};
use netdxf::tables::{Layer, TableObject, UCS};
use netdxf::{AciColor, DxfDocument, Transparency, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn ucs_and_layer_state_conformance() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let mut ucs = UCS::from_origin_axes(
                "SURVEY_BASE",
                Vector3::new(100.0, 200.0, 300.0),
                Vector3::UNIT_X,
                Vector3::UNIT_Y,
            )
            .unwrap();
            ucs.elevation = -8.5;
            doc.ucss.add(ucs);

            let mut layer = Layer::new("STATE_LAYER").unwrap();
            layer.color = AciColor::from_rgb(10, 20, 30);
            layer.transparency = Transparency::new(40).unwrap();
            layer.is_frozen = true;
            layer.is_locked = true;
            let snapshot = LayerStateProperties::from(&layer);
            doc.layers.add(layer);

            let mut state = LayerState::new("ARCH_STATE").unwrap();
            state.properties.insert("STATE_LAYER".into(), snapshot);
            assert_eq!(state.name(), "ARCH_STATE");
            assert!(state.properties.get("STATE_LAYER").unwrap().is_frozen);

            let bytes = doc.save_to_bytes(binary).expect("save UCS");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load UCS");
            let loaded_ucs = loaded.ucss.get("SURVEY_BASE").expect("SURVEY_BASE UCS");
            assert_eq!(loaded_ucs.origin, Vector3::new(100.0, 200.0, 300.0));
            assert!((loaded_ucs.elevation - (-8.5)).abs() < 1e-12);
        }
    }
}
