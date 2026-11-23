use netdxf::entities::{EntityObject, Spline};
use netdxf::DxfDocument;

use super::helpers::{make_rational_bezier, SUPPORTED_VERSIONS};

#[test]
fn spline_degree_elevation_conformance() {
    for degree in 1i16..=6 {
        let bez = make_rational_bezier(degree);
        let bez_pts = bez.polygonal_vertexes(33);
        let mut elevated = bez.clone();
        assert!(elevated.elevate_degree());
        assert_eq!(elevated.degree, degree + 1);
        let el_pts = elevated.polygonal_vertexes(33);
        for (a, b) in bez_pts.iter().zip(el_pts.iter()) {
            assert!((*a - *b).modulus() < 1e-10, "degree={degree} elevate drift");
        }
    }

    let mut elevated = make_rational_bezier(3);
    elevated.elevate_degree();
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            doc.add_entity(elevated.clone());
            let bytes = doc.save_to_bytes(binary).expect("save elevated spline");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load elevated spline");
            let s: &Spline = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Spline(s) => Some(s.as_ref()),
                    _ => None,
                })
                .unwrap();
            assert_eq!(s.degree, 4);
        }
    }
}
