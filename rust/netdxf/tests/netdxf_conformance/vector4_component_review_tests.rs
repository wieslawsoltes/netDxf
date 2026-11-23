use netdxf::Vector4;

use super::helpers::vec4_bits;

#[test]
fn vector4_component_review() {
    let specials = [
        0.0,
        -0.0,
        f64::from_bits(1),
        -f64::from_bits(1),
        f64::MAX,
        -f64::MAX,
        f64::INFINITY,
        f64::NEG_INFINITY,
        f64::from_bits(0x7ff8_0000_0000_0042),
        f64::from_bits(0xfff8_0000_0000_0021),
    ];
    for &sp in &specials {
        for c in 0..4 {
            let mut vals = [1.0, -2.0, 3.0, -4.0];
            vals[c] = sp;
            let v = Vector4::from_array(vals);
            let before = vec4_bits(v);
            let arr = v.to_array();
            assert_eq!(arr.map(f64::to_bits), before);
            assert_eq!(vec4_bits(Vector4::from_array(arr)), before);
        }
    }

    for i in 0..128 {
        let a: Vec<f64> = (0..4)
            .map(|c| (((i + 1) * (17 + c * 12) + (31 + c * 8)) % 97) as f64 - 48.0)
            .collect();
        let b: Vec<f64> = (0..4)
            .map(|c| (((i + 1) * (17 + c * 12) + 2 * (31 + c * 8)) % 97) as f64 - 48.0)
            .collect();
        let u = Vector4::new(a[0], a[1], a[2], a[3]);
        let v = Vector4::new(b[0], b[1], b[2], b[3]);
        let exact: i64 = (0..4)
            .map(|c| {
                let d = a[c] as i64 - b[c] as i64;
                d * d
            })
            .sum();
        assert_eq!(Vector4::square_distance(u, v), exact as f64);
        assert_eq!(Vector4::square_distance(v, u), exact as f64);
        assert_eq!(Vector4::distance(u, v), (exact as f64).sqrt());
        assert_eq!(Vector4::square_distance(u, u), 0.0);
    }

    for i in 0..32 {
        let z = (i - 16) as f64;
        let w = (i % 5 - 2) as f64;
        let a = Vector4::new(1.0, 2.0, z, w);
        let b = Vector4::new(1.0, 2.0, z, w + 3.0);
        assert_eq!(Vector4::square_distance(a, b), 9.0);
        assert_eq!(Vector4::distance(a, b), 3.0);
    }
}
