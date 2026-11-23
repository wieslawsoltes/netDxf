use super::helpers::make_rational_spline;

#[test]
fn spline_trim_conformance() {
    for degree in 1i16..=6 {
        let source = make_rational_spline(degree);
        for (a, b) in [(0.125, 0.875), (0.0, 0.5), (0.5, 1.0)] {
            let trimmed = source.trim(a, b).expect("trim spline");
            assert_eq!(trimmed.degree, degree);
            for i in 0..=8 {
                let u = a + (b - a) * (i as f64 / 8.0);
                let p0 = source.evaluate(u).unwrap();
                let p1 = trimmed.evaluate(u).unwrap();
                assert!((p0 - p1).modulus() < 1e-11, "degree={degree} trim drift at u={u}");
            }
        }
        assert!(source.trim(0.5, 0.5).is_none());
        assert!(source.trim(0.8, 0.2).is_none());
        assert!(source.trim(f64::NAN, 0.5).is_none());
    }
}
