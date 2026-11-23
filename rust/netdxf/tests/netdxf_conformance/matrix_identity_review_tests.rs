use netdxf::{Matrix3, Matrix4, Vector3, Vector4};

use super::helpers::{scale_b, vec3_bits, vec4_bits};

#[test]
fn matrix3_and_matrix4_identity_review() {
    for index in 0..9 {
        for variant in 0..3 {
            let mut a = [0.0; 9];
            a[0] = 1.0;
            a[4] = 1.0;
            a[8] = 1.0;
            let delta = match variant {
                0 => scale_b(1.0, -45),
                1 => -scale_b(1.0, -45),
                _ => 1e-13,
            };
            a[index] += delta;
            let m = Matrix3::new(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8]);
            assert!(m.is_identity_with(1e-6));
            assert!(!m.is_identity_exact());

            let p = Vector3::new(scale_b(1.0, 40), scale_b(-2.0, 40), scale_b(3.0, 40));
            let res = m * p;
            assert_eq!(res.x, a[0] * p.x + a[1] * p.y + a[2] * p.z);
            assert_eq!(res.y, a[3] * p.x + a[4] * p.y + a[5] * p.z);
            assert_eq!(res.z, a[6] * p.x + a[7] * p.y + a[8] * p.z);

            let row = index / 3;
            let col = index % 3;
            let expected_det = if row == col { a[index] } else { 1.0 };
            assert_eq!(m.determinant(), expected_det);

            let inv = m.inverse().unwrap();
            let inv_arr = [
                inv.m11, inv.m12, inv.m13, inv.m21, inv.m22, inv.m23, inv.m31, inv.m32, inv.m33,
            ];
            let expected_inv_entry = if row == col { 1.0 / a[index] } else { -a[index] };
            assert!((inv_arr[index] - expected_inv_entry).abs() <= expected_inv_entry.abs() * 2e-15);
        }
    }

    for index in 0..16 {
        let mut rows = Matrix4::IDENTITY.m;
        let row = index / 4;
        let col = index % 4;
        rows[row][col] += scale_b(1.0, -45);
        let m = Matrix4::from_rows(rows);
        assert!(m.is_identity_with(1e-6));
        assert!(!m.is_identity_exact());
        let expected_det = if row == col { rows[row][col] } else { 1.0 };
        assert_eq!(m.determinant(), expected_det);
        let inv = m.inverse().unwrap();
        let expected_inv = if row == col {
            1.0 / rows[row][col]
        } else {
            -rows[row][col]
        };
        assert!((inv.m[row][col] - expected_inv).abs() <= expected_inv.abs() * 2e-15);
    }

    let mut m3 = Matrix3::IDENTITY;
    m3.m12 = -0.0;
    assert!(m3.is_identity_exact());
    let p3 = Vector3::new(-0.0, f64::NAN, f64::INFINITY);
    assert_eq!(vec3_bits(p3), vec3_bits(m3 * p3));

    let mut m4 = Matrix4::IDENTITY;
    m4.m[0][3] = -0.0;
    assert!(m4.is_identity_exact());
    let p4 = Vector4::new(-0.0, f64::NAN, f64::INFINITY, f64::NEG_INFINITY);
    assert_eq!(vec4_bits(p4), vec4_bits(m4 * p4));
}
