use super::helpers::make_rational_spline;

#[test]
fn spline_split_conformance() {
    for degree in 1i16..=6 {
        let source = make_rational_spline(degree);
        let (left, right) = source.split(0.375).expect("split spline");
        assert_eq!(left.degree, degree);
        assert_eq!(right.degree, degree);
        let mid_expected = source.evaluate(0.375).expect("eval mid");
        let left_end = left.evaluate(0.375).expect("eval left end");
        let right_start = right.evaluate(0.375).expect("eval right start");
        assert!((mid_expected - left_end).modulus() < 1e-11);
        assert!((mid_expected - right_start).modulus() < 1e-11);
        assert!(source.split(0.0).is_none());
        assert!(source.split(1.0).is_none());
        assert!(source.split(f64::INFINITY).is_none());
    }
}
