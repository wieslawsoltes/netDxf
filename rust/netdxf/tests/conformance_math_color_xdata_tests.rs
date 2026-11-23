use netdxf::blocks::Block;
use netdxf::entities::{Arc, Circle, EntityObject, Insert, Line, MText, Text};
use netdxf::gte::GVector;
use netdxf::header::DxfVersion;
use netdxf::tables::{ApplicationRegistry, Layer};
use netdxf::{
    AciColor, CoordinateSystem, DxfDocument, MathHelper, Matrix3, Matrix4, Transparency, Vector2,
    Vector3, Vector4, XData, XDataCode, XDataRecord, XDataValue,
};

const SUPPORTED_VERSIONS: [DxfVersion; 6] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
    DxfVersion::AutoCad2013,
    DxfVersion::AutoCad2018,
];

fn scale_b(value: f64, exp: i32) -> f64 {
    // Exact binary64 power-of-two scaling (handles subnormals down to 2^-1074 without powi overflow).
    let mut v = value;
    let mut e = exp;
    while e > 1023 {
        v *= f64::from_bits(0x7fe0_0000_0000_0000); // 2^1023
        e -= 1023;
    }
    while e < -1022 {
        v *= f64::from_bits(0x0010_0000_0000_0000); // 2^-1022
        e += 1022;
    }
    let scale = f64::from_bits(((e + 1023) as u64) << 52);
    v * scale
}

fn vec3_bits(v: Vector3) -> [u64; 3] {
    [v.x.to_bits(), v.y.to_bits(), v.z.to_bits()]
}

fn vec4_bits(v: Vector4) -> [u64; 4] {
    [v.x.to_bits(), v.y.to_bits(), v.z.to_bits(), v.w.to_bits()]
}

fn assert_ocs_component(expected: f64, actual: f64) {
    assert!(actual.is_finite(), "OCS component is nonfinite: {actual}");
    let tol = (f64::from_bits(1) * 16.0).max(expected.abs() * 4e-15);
    assert!(
        (expected - actual).abs() <= tol,
        "OCS component changed: expected={expected:?}, actual={actual:?}, tol={tol:?}"
    );
}

#[test]
fn arbitrary_axis_review_extremes_and_thresholds() {
    let mut inputs = Vec::new();
    for &exp in &[
        -1074, -1022, -1000, -600, -500, -50, 0, 50, 500, 600, 1000, 1021,
    ] {
        let s = scale_b(1.0, exp);
        inputs.push(Vector3::new(s, -2.0 * s, 3.0 * s));
        inputs.push(Vector3::new(0.0, 0.0, s));
        inputs.push(Vector3::new(0.0, 0.0, -s));
    }
    inputs.extend_from_slice(&[
        Vector3::new(1e-13, 2e-13, 1.0),
        Vector3::new(-1e-13, -2e-13, 1.0),
        Vector3::new(1e-13, 0.0, -1.0),
        Vector3::new(0.0, 1e-13, 1.0),
        Vector3::new(0.0, 1.0, 0.0),
        Vector3::new(1.0, 0.0, 0.0),
        Vector3::new(f64::MAX, f64::MAX, f64::MAX),
        Vector3::new(f64::from_bits(1), 0.0, f64::MAX),
        Vector3::new(0.015624, 0.015624, (1.0 - 2.0 * 0.015624 * 0.015624_f64).sqrt()),
        Vector3::new(
            0.015626,
            0.015624,
            (1.0 - 0.015626 * 0.015626 - 0.015624 * 0.015624_f64).sqrt(),
        ),
        Vector3::new(
            0.015624,
            0.015626,
            (1.0 - 0.015624 * 0.015624 - 0.015626 * 0.015626_f64).sqrt(),
        ),
    ]);

    for source in inputs {
        let basis = MathHelper::try_arbitrary_axis(source).expect("valid finite non-zero axis");
        let x = Vector3::new(basis.m11, basis.m21, basis.m31);
        let y = Vector3::new(basis.m12, basis.m22, basis.m32);
        let z = Vector3::new(basis.m13, basis.m23, basis.m33);

        let scale = source.x.abs().max(source.y.abs()).max(source.z.abs());
        let scaled = Vector3::new(source.x / scale, source.y / scale, source.z / scale);
        let len = (scaled.x * scaled.x + scaled.y * scaled.y + scaled.z * scaled.z).sqrt();
        let expected_z = Vector3::new(scaled.x / len, scaled.y / len, scaled.z / len);

        assert_ocs_component(expected_z.x, z.x);
        assert_ocs_component(expected_z.y, z.y);
        assert_ocs_component(expected_z.z, z.z);

        for axis in [x, y, z] {
            assert!((Vector3::dot_product(axis, axis) - 1.0).abs() <= 4e-15);
        }
        assert!(Vector3::dot_product(x, y).abs() <= 4e-15);
        assert!(Vector3::dot_product(x, z).abs() <= 4e-15);
        assert!(Vector3::dot_product(y, z).abs() <= 4e-15);
        assert!((Vector3::cross_product(x, y) - z).modulus() <= 4e-15);

        let reference = if expected_z.x.abs() < 1.0 / 64.0 && expected_z.y.abs() < 1.0 / 64.0 {
            Vector3::UNIT_Y
        } else {
            Vector3::UNIT_Z
        };
        let wanted_x = Vector3::cross_product(reference, expected_z);
        let x_len = Vector3::dot_product(wanted_x, wanted_x).sqrt();
        assert_ocs_component(wanted_x.x / x_len, x.x);
        assert_ocs_component(wanted_x.y / x_len, x.y);
        assert_ocs_component(wanted_x.z / x_len, x.z);

        let point = Vector3::new(2.0, -3.0, 5.0);
        let world = MathHelper::transform_3d(
            point,
            source,
            CoordinateSystem::Object,
            CoordinateSystem::World,
        );
        let back = MathHelper::transform_3d(
            world,
            source,
            CoordinateSystem::World,
            CoordinateSystem::Object,
        );
        assert!((back - point).modulus() <= 1e-13);
        assert_eq!(
            vec3_bits(world),
            vec3_bits(MathHelper::transform_ocs_to_wcs(
                Vector2::new(2.0, -3.0),
                source,
                5.0
            ))
        );
        let (local, elev) = MathHelper::transform_wcs_to_ocs(world, source);
        assert!((local.x - 2.0).abs() <= 1e-13);
        assert!((local.y + 3.0).abs() <= 1e-13);
        assert!((elev - 5.0).abs() <= 1e-13);
    }
}

#[test]
fn arbitrary_axis_rejects_zero_and_non_finite() {
    for mask in 0..8 {
        let v = Vector3::new(
            if (mask & 1) == 0 { 0.0 } else { -0.0 },
            if (mask & 2) == 0 { 0.0 } else { -0.0 },
            if (mask & 4) == 0 { 0.0 } else { -0.0 },
        );
        assert!(MathHelper::try_arbitrary_axis(v).is_err());
    }
    for bad in [f64::NAN, f64::INFINITY, f64::NEG_INFINITY] {
        for axis in 0..3 {
            let mut v = Vector3::new(1.0, 2.0, 3.0);
            v[axis] = bad;
            assert!(MathHelper::try_arbitrary_axis(v).is_err());
        }
    }
}

#[test]
fn arbitrary_axis_wire_roundtrip_all_versions() {
    let normals = [
        Vector3::new(1e-13, 2e-13, 1.0),
        Vector3::new(-1e-13, -2e-13, 1.0),
        Vector3::new(1e-13, 0.0, -1.0),
        Vector3::new(0.0, 1e-13, 1.0),
        Vector3::new(0.0, 1.0, 0.0),
        Vector3::new(2.0, 3.0, 6.0),
    ];
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            for &nested in &[false, true] {
                for &normal in &normals {
                    let mut circle = Circle::new(Vector3::new(1e12, -2e12, 3e12), 7.0);
                    circle.common.normal = normal;
                    let mut text = Text::new("OCS tilt", Vector3::new(-4e12, 5e12, -6e12), 2.0);
                    text.common.normal = normal;
                    text.rotation = 37.0;
                    let mut arc = Arc::new(Vector3::new(7e12, -8e12, 9e12), 5.0, 20.0, 210.0);
                    arc.common.normal = normal;

                    let expected_circle = circle.center;
                    let expected_text = text.position;
                    let expected_arc = arc.center;

                    let mut doc = DxfDocument::with_version(version);
                    if nested {
                        let mut block = Block::new("AXIS_REVIEW").unwrap();
                        block.add_entity(circle);
                        block.add_entity(text);
                        block.add_entity(arc);
                        doc.blocks.add(block);
                        doc.add_entity(Insert::new("AXIS_REVIEW"));
                    } else {
                        doc.add_entity(circle);
                        doc.add_entity(text);
                        doc.add_entity(arc);
                    }

                    let bytes = doc.save_to_bytes(binary).expect("save OCS wire");
                    let loaded = DxfDocument::load_from_bytes(&bytes).expect("load OCS wire");
                    let entities: &[EntityObject] = if nested {
                        &loaded.blocks.get("AXIS_REVIEW").unwrap().entities
                    } else {
                        loaded.entities.all()
                    };
                    let c = entities
                        .iter()
                        .find_map(|e| match e {
                            EntityObject::Circle(c) => Some(c),
                            _ => None,
                        })
                        .unwrap();
                    let t = entities
                        .iter()
                        .find_map(|e| match e {
                            EntityObject::Text(t) => Some(t),
                            _ => None,
                        })
                        .unwrap();
                    let a = entities
                        .iter()
                        .find_map(|e| match e {
                            EntityObject::Arc(a) => Some(a),
                            _ => None,
                        })
                        .unwrap();
                    assert!((c.center - expected_circle).modulus() <= 0.02);
                    assert!((t.position - expected_text).modulus() <= 0.02);
                    assert!((a.center - expected_arc).modulus() <= 0.02);
                }
            }
        }
    }
}

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

#[test]
fn true_color_packed_and_wire_review() {
    let color = AciColor::from_rgb(18, 171, 52);
    assert_eq!(color.to_true_color(), 0x12AB34);
    assert_eq!(color.to_packed_true_color(), 0xC212_AB34_u32 as i32);

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let mut layer = Layer::new("REVIEW_RGB").unwrap();
            layer.color = color;
            doc.layers.add(layer);
            let mut line = Line::new(Vector3::ZERO, Vector3::UNIT_X);
            line.common.color = color;
            line.common.layer = "REVIEW_RGB".into();
            doc.add_entity(line);

            let bytes = doc.save_to_bytes(binary).expect("save truecolor");
            if !binary {
                let text = String::from_utf8_lossy(&bytes);
                let has_420 = text
                    .lines()
                    .collect::<Vec<_>>()
                    .windows(2)
                    .any(|w| w[0].trim() == "420" && w[1].trim() == format!("{}", 0x12AB34));
                if version == DxfVersion::AutoCad2000 {
                    assert!(!has_420, "R2000 must not emit group 420");
                } else {
                    assert!(
                        has_420,
                        "{version:?} should emit untagged RGB 0x12AB34 in group 420"
                    );
                }
            }
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load truecolor");
            let loaded_line = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) => Some(l),
                    _ => None,
                })
                .unwrap();
            if version >= DxfVersion::AutoCad2004 {
                assert!(loaded_line.common.color.use_true_color);
                assert_eq!(loaded_line.common.color.to_rgb(), [18, 171, 52]);
            }
        }
    }
}

#[test]
fn transparency_stored_and_wire_conformance() {
    assert_eq!(Transparency::default().stored_alpha_value(), None);
    assert_eq!(Transparency::new(0).unwrap().stored_alpha_value(), None);
    assert_eq!(Transparency::BY_BLOCK.stored_alpha_value(), None);
    assert_eq!(Transparency::BY_LAYER.stored_alpha_value(), None);
    assert_eq!(Transparency::BY_BLOCK.to_alpha_value(), 0x0100_0000);
    assert_eq!(Transparency::new(0).unwrap().to_alpha_value(), 0x0200_00FF);

    let mut packed_samples = Vec::new();
    for mode in [0x0100_0000_i32, 0x0200_0000_i32] {
        for alpha in 0..256 {
            packed_samples.push(mode | alpha);
        }
    }
    packed_samples.extend_from_slice(&[
        0,
        -1,
        i32::MIN,
        i32::MAX,
        0x0300_007F,
        0x0012_3456,
        0xA512_3481_u32 as i32,
        0x4200_0000,
    ]);

    for packed in packed_samples {
        let mut item = Transparency::from_alpha_value(packed);
        assert_eq!(item.stored_alpha_value(), Some(packed));
        assert_eq!(item.to_alpha_value(), packed);

        let pct = (100.0 - ((packed & 255) as f64 / 255.0) * 100.0) as i16;
        let legacy = Transparency::from_cad_index(pct);
        assert_eq!(item.value(), legacy.value());
        assert_eq!(item.is_by_block(), legacy.is_by_block());
        assert_eq!(item, legacy);

        assert!(item.set_value(-1).is_err());
        assert!(item.set_value(91).is_err());
        assert_eq!(item.stored_alpha_value(), Some(packed));

        item.set_value(25).unwrap();
        assert_eq!(item.stored_alpha_value(), None);
        assert_eq!(
            item.to_alpha_value(),
            Transparency::new(25).unwrap().to_alpha_value()
        );
    }

    let state_zero = Transparency::from_layer_state_alpha_value(0);
    assert_eq!(state_zero.stored_alpha_value(), Some(0));
    assert_eq!(state_zero.value(), 0);

    let wire_values = [
        0x0200_0000_i32,
        0x0200_0001,
        0x0200_0080,
        0x0200_00FE,
        0x0200_00FF,
        0x0100_0000,
    ];
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            for (i, &packed) in wire_values.iter().enumerate() {
                let mut line = Line::new(
                    Vector3::new(i as f64, 0.0, 0.0),
                    Vector3::new(i as f64, 1.0, 0.0),
                );
                line.common.transparency = Transparency::from_alpha_value(packed);
                doc.add_entity(line);

                let mut mtext =
                    MText::new(format!("packed {i}"), Vector3::new(i as f64, 2.0, 0.0), 1.0);
                mtext.common.transparency = Transparency::from_alpha_value(packed);
                doc.add_entity(mtext);
            }
            let bytes = doc.save_to_bytes(binary).expect("save transparency wire");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load transparency wire");
            let mut loaded_line_alphas: Vec<i32> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Line(l) => l.common.transparency.stored_alpha_value(),
                    _ => None,
                })
                .collect();
            loaded_line_alphas.sort();
            let mut expected = wire_values.to_vec();
            expected.sort();
            assert_eq!(
                loaded_line_alphas, expected,
                "version={version:?}, binary={binary}"
            );
        }
    }
}

#[test]
fn xdata_clone_and_wire_isolation() {
    const APP: &str = "DXF_CLONE_CONFORMANCE";

    for len in [0usize, 1, 127] {
        let bytes: Vec<u8> = (0..len).map(|i| i as u8).collect();
        let mut src = XData::new(APP);
        src.x_data_record.push(XDataRecord::new(
            XDataCode::BinaryData,
            XDataValue::Binary(bytes.clone()),
        ));
        let mut copy = src.clone();
        if let XDataValue::Binary(ref mut b) = copy.x_data_record[0].value {
            if !b.is_empty() {
                b[0] = 200;
            }
        }
        if let XDataValue::Binary(ref orig) = src.x_data_record[0].value {
            assert_eq!(orig, &bytes);
        } else {
            panic!("expected BinaryData");
        }
    }

    let mut scalar = XData::new(APP);
    scalar.x_data_record.push(XDataRecord::open_control_string());
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::DatabaseHandle,
        XDataValue::String("ABC123".into()),
    ));
    scalar
        .x_data_record
        .push(XDataRecord::string("Zażółć gęślą jaźń"));
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::LayerName,
        XDataValue::String("Geometry".into()),
    ));
    scalar.x_data_record.push(XDataRecord::int16(-42));
    scalar.x_data_record.push(XDataRecord::int32(-123456));
    scalar.x_data_record.push(XDataRecord::real(12.75));
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::Distance,
        XDataValue::Real(3.5),
    ));
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::ScaleFactor,
        XDataValue::Real(2.0),
    ));
    scalar
        .x_data_record
        .push(XDataRecord::close_control_string());
    let cloned_scalar = scalar.clone();
    assert_eq!(scalar.x_data_record, cloned_scalar.x_data_record);
    assert_eq!(scalar.x_data_record[0].code, XDataCode::ControlString);

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut src_line = Line::new(Vector3::ZERO, Vector3::UNIT_X);
            let mut xd = XData::new(APP);
            xd.x_data_record.push(XDataRecord::new(
                XDataCode::BinaryData,
                XDataValue::Binary(vec![10, 77, 30]),
            ));
            src_line.common.object.add_xdata(xd);

            let mut copy_line = src_line.clone();
            copy_line.start_point = Vector3::new(5.0, 0.0, 0.0);
            if let XDataValue::Binary(ref mut b) =
                copy_line.common.object.x_data.get_mut(APP).unwrap().x_data_record[0].value
            {
                b[0] = 99;
                b[1] = 20;
            }

            let mut doc = DxfDocument::with_version(version);
            doc.application_registries
                .add(ApplicationRegistry::new(APP).unwrap());
            doc.add_entity(src_line);
            doc.add_entity(copy_line);

            let bytes = doc.save_to_bytes(binary).expect("save xdata clone doc");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load xdata clone doc");
            let orig = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) if l.start_point.x == 0.0 => Some(l),
                    _ => None,
                })
                .unwrap();
            let cloned = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) if l.start_point.x == 5.0 => Some(l),
                    _ => None,
                })
                .unwrap();
            assert_eq!(
                orig.common.object.x_data.get(APP).unwrap().x_data_record[0].value,
                XDataValue::Binary(vec![10, 77, 30])
            );
            assert_eq!(
                cloned.common.object.x_data.get(APP).unwrap().x_data_record[0].value,
                XDataValue::Binary(vec![99, 20, 30])
            );
        }
    }
}
