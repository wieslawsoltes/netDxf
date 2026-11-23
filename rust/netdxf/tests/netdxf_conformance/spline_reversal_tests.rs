use super::helpers::make_rational_spline;

#[test]
fn spline_reversal_conformance() {
    for degree in 1i16..=6 {
        let source = make_rational_spline(degree);
        let orig_pts = source.polygonal_vertexes(33);
        let mut rev = source.clone();
        rev.reverse();
        let rev_pts = rev.polygonal_vertexes(33);
        for (a, b) in orig_pts.iter().zip(rev_pts.iter().rev()) {
            assert!((*a - *b).modulus() < 1e-11, "degree={degree} reverse drift");
        }
    }
}
