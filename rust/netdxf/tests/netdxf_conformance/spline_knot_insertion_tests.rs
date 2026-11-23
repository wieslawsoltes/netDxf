use netdxf::entities::{EntityObject, Spline};
use netdxf::DxfDocument;

use super::helpers::{make_rational_spline, SUPPORTED_VERSIONS};

#[test]
fn spline_knot_insertion_conformance() {
    for degree in 1i16..=6 {
        let source = make_rational_spline(degree);
        let orig_pts = source.polygonal_vertexes(33);

        let mut refined = source.clone();
        assert!(refined.insert_knot(0.125, 1));
        assert_eq!(refined.control_points.len(), source.control_points.len() + 1);
        assert_eq!(refined.knots.len(), source.knots.len() + 1);
        let ref_pts = refined.polygonal_vertexes(33);
        for (a, b) in orig_pts.iter().zip(ref_pts.iter()) {
            assert!((*a - *b).modulus() < 1e-11, "degree={degree} insert_knot drift");
        }

        let mut bad = source.clone();
        assert!(!bad.insert_knot(f64::NAN, 1));
        assert!(!bad.insert_knot(-0.5, 1));
        assert!(!bad.insert_knot(1.5, 1));
        assert!(!bad.insert_knot(0.125, 0));
    }

    let base = make_rational_spline(3);
    let mut refined = base.clone();
    refined.insert_knot(0.25, 2);
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            doc.add_entity(base.clone());
            doc.add_entity(refined.clone());
            let bytes = doc.save_to_bytes(binary).expect("save refined spline");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load refined spline");
            let splines: Vec<&Spline> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Spline(s) => Some(s.as_ref()),
                    _ => None,
                })
                .collect();
            assert_eq!(splines.len(), 2);
            let p0 = splines[0].evaluate(0.4).unwrap();
            let p1 = splines[1].evaluate(0.4).unwrap();
            assert!((p0 - p1).modulus() < 1e-10);
        }
    }
}
