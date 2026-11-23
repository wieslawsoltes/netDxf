use netdxf::blocks::Block;
use netdxf::entities::{Arc, Circle, EntityObject, Insert, Text};
use netdxf::{CoordinateSystem, DxfDocument, MathHelper, Vector2, Vector3};

use super::helpers::{scale_b, vec3_bits, SUPPORTED_VERSIONS};

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
        Vector3::new(
            0.015624,
            0.015624,
            (1.0 - 2.0 * 0.015624 * 0.015624_f64).sqrt(),
        ),
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
