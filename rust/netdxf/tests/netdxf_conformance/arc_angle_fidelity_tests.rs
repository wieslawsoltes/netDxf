use netdxf::entities::{Arc, EntityObject};
use netdxf::{DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn arc_angle_fidelity_and_wire_roundtrip() {
    let pairs = [
        (1e-9, 2e-9),
        (0.25, 90.0),
        (30.0, 210.0),
        (90.0, 180.0),
        (359.5, 0.5),
    ];
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            for (start, end) in pairs {
                let mut arc = Arc::new(Vector3::new(1.0, 2.0, 3.0), 4.0, start, end);
                arc.thickness = 2.0;
                doc.add_entity(arc);
            }
            let bytes = doc.save_to_bytes(binary).expect("save arc angles");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load arc angles");
            let arcs: Vec<&Arc> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Arc(a) => Some(a.as_ref()),
                    _ => None,
                })
                .collect();
            assert_eq!(arcs.len(), pairs.len());
            for (arc, (start, end)) in arcs.iter().zip(pairs.iter()) {
                assert!((arc.start_angle - start).abs() < 1e-11);
                assert!((arc.end_angle - end).abs() < 1e-11);
                assert!((arc.radius - 4.0).abs() < 1e-12);
                assert!((arc.thickness - 2.0).abs() < 1e-12);
                assert!((arc.center - Vector3::new(1.0, 2.0, 3.0)).modulus() < 1e-11);
            }
        }
    }
}
