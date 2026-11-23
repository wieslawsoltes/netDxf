use netdxf::gte::GVector;

use super::helpers::scale_b;

#[test]
fn gvector_extremes_and_safety_conformance() {
    let points = [[3.0, 5.0], [-2.0, 7.0], [9.0, -4.0]];
    let orders = [
        [0, 1, 2],
        [0, 2, 1],
        [1, 0, 2],
        [1, 2, 0],
        [2, 0, 1],
        [2, 1, 0],
    ];
    for order in orders {
        let vecs: Vec<GVector> = order
            .iter()
            .map(|&i| GVector::from_vec(points[i].to_vec()))
            .collect();
        let (min_v, max_v) = GVector::compute_extremes(&vecs).unwrap();
        assert_eq!(min_v.as_slice(), &[-2.0, -4.0]);
        assert_eq!(max_v.as_slice(), &[9.0, 7.0]);
    }

    let patterns: [u64; 10] = [
        0xfff0_0000_0000_0000,
        0xffef_ffff_ffff_ffff,
        0x8000_0000_0000_0001,
        0x8000_0000_0000_0000,
        0x0000_0000_0000_0000,
        0x0000_0000_0000_0001,
        0x7fef_ffff_ffff_ffff,
        0x7ff0_0000_0000_0000,
        0x7ff8_0000_0000_0001,
        0xfff8_0000_0000_0042,
    ];
    for &pa in &patterns {
        for &pb in &patterns {
            let x = f64::from_bits(pa);
            let y = f64::from_bits(pb);
            let vecs = [GVector::from_vec(vec![x, y]), GVector::from_vec(vec![y, x])];
            let (min_v, max_v) = GVector::compute_extremes(&vecs).unwrap();
            for c in 0..2 {
                let first = vecs[0][c];
                let second = vecs[1][c];
                let expected_min = if first.is_nan() {
                    first
                } else if second < first {
                    second
                } else {
                    first
                };
                let expected_max = if first.is_nan() {
                    first
                } else if second > first {
                    second
                } else {
                    first
                };
                assert_eq!(expected_min.to_bits(), min_v[c].to_bits());
                assert_eq!(expected_max.to_bits(), max_v[c].to_bits());
            }
        }
    }

    let mut cases: Vec<Vec<f64>> = Vec::new();
    for &exp in &[
        -1074, -1073, -1068, -1023, -1022, -1000, -500, 0, 100, 500, 1000, 1020,
    ] {
        let s = scale_b(1.0, exp);
        cases.push(vec![3.0 * s, 4.0 * s, 0.0]);
        cases.push(vec![-3.0 * s, -4.0 * s, 0.0]);
    }
    cases.push(vec![]);
    cases.push(vec![0.0, -0.0]);
    cases.push(vec![f64::MAX, f64::MAX]);
    cases.push(vec![1e300, -1e-300, 1e299, 0.0]);

    for input in cases {
        let source = GVector::from_vec(input.clone());
        let len = source.length_robust(true);
        let (norm, orig) = source.normalize(true);
        assert_eq!(len.to_bits(), orig.to_bits());
        if input.iter().any(|&x| x != 0.0) {
            assert!((GVector::dot(&norm, &norm) - 1.0).abs() <= 2e-15);
        } else {
            assert_eq!(orig, 0.0);
            assert!(norm.as_slice().iter().all(|&x| x == 0.0));
        }
    }

    let mut basis = [
        GVector::from_vec(vec![3.0, 4.0]),
        GVector::from_vec(vec![-4.0, 3.0]),
    ];
    let shortest = GVector::orthonormalize(&mut basis, true);
    assert!((shortest - 5.0).abs() <= 2e-15);
    assert!((GVector::dot(&basis[0], &basis[0]) - 1.0).abs() <= 2e-15);
    assert!((GVector::dot(&basis[1], &basis[1]) - 1.0).abs() <= 2e-15);
    assert!(GVector::dot(&basis[0], &basis[1]).abs() <= 2e-15);
}
