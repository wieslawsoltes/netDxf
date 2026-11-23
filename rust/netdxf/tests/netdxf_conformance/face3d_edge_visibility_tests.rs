use netdxf::entities::{EntityObject, Face3D, Face3DEdgeFlags};
use netdxf::{DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn face3d_edge_visibility_all_16_masks() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            for mask in 0i16..16 {
                let mut face = Face3D::new(
                    Vector3::new(1.0, 2.0, 3.0),
                    Vector3::new(4.0, 5.0, 6.0),
                    Vector3::new(7.0, 8.0, 9.0),
                    Vector3::new(10.0, 11.0, 12.0),
                );
                face.edge_flags = Face3DEdgeFlags::from_bits_truncate(mask);
                doc.add_entity(face);
            }
            let bytes = doc.save_to_bytes(binary).expect("save face3d masks");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load face3d masks");
            let faces: Vec<&Face3D> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Face3D(f) => Some(f.as_ref()),
                    _ => None,
                })
                .collect();
            assert_eq!(faces.len(), 16);
            for (mask, face) in (0i16..16).zip(faces.iter()) {
                assert_eq!(face.edge_flags.bits(), mask);
            }
        }
    }
}
