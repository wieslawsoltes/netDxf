use std::time::{Duration, UNIX_EPOCH};

use netdxf::units::{DrawingTime, DrawingUnits, UnitHelper};
use netdxf::{
    AciColor, BezierCurveCubic, BezierCurveQuadratic, BoundingRectangle, CoordinateSystem,
    Lineweight, MathHelper, Matrix2, Matrix3, Matrix4, Transparency, Vector2, Vector3, Vector4,
};

#[test]
fn vector2_and_vector3_operations() {
    let a = Vector2::new(3.0, 4.0);
    assert!(MathHelper::is_equal(a.modulus(), 5.0));
    let n = Vector2::normalize(a);
    assert!(MathHelper::is_equal(n.modulus(), 1.0));
    assert!(Vector2::are_perpendicular(
        Vector2::UNIT_X,
        Vector2::UNIT_Y,
        MathHelper::DEFAULT_EPSILON
    ));
    assert!(Vector2::are_parallel(
        Vector2::new(2.0, 4.0),
        Vector2::new(-1.0, -2.0),
        MathHelper::DEFAULT_EPSILON
    ));

    let u = Vector3::new(1.0, 0.0, 0.0);
    let v = Vector3::new(0.0, 1.0, 0.0);
    assert_eq!(Vector3::cross_product(u, v), Vector3::UNIT_Z);
    assert!(MathHelper::is_equal(
        Vector3::angle_between(u, v),
        MathHelper::HALF_PI
    ));

    let v4 = Vector4::new(1.0, 2.0, 2.0, 0.0);
    assert!(MathHelper::is_equal(v4.modulus(), 3.0));
}

#[test]
fn matrices_invert_and_multiply() {
    let m2 = Matrix2::rotation(MathHelper::HALF_PI);
    let rot_x = m2 * Vector2::UNIT_X;
    assert!(rot_x.equals_with(Vector2::UNIT_Y, 1e-12));
    let inv2 = m2.inverse().unwrap();
    let id2 = m2 * inv2;
    assert!(MathHelper::is_equal(id2.m11, 1.0));
    assert!(MathHelper::is_equal(id2.m22, 1.0));

    let m3 = Matrix3::rotation_z(MathHelper::HALF_PI) * Matrix3::scale(2.0, 3.0, 4.0);
    let inv3 = m3.inverse().unwrap();
    assert!((m3 * inv3).is_identity());

    let m4 = Matrix4::from_affine(Matrix3::IDENTITY, Vector3::new(10.0, 20.0, 30.0));
    let p = m4 * Vector4::new(1.0, 2.0, 3.0, 1.0);
    assert_eq!(p, Vector4::new(11.0, 22.0, 33.0, 1.0));
}

#[test]
fn arbitrary_axis_and_ocs_wcs_roundtrip() {
    // Test both branches of the Arbitrary Axis Algorithm (near-Z pole < 1/64 and general normal)
    let normals = [
        Vector3::UNIT_Z,
        Vector3::new(0.0, 0.0, -1.0),
        Vector3::new(0.005, -0.005, 1.0),
        Vector3::new(1.0, 2.0, 3.0),
        Vector3::new(-3.0, 4.0, -5.0),
    ];
    let pt = Vector3::new(12.5, -7.25, 3.75);
    for normal in normals {
        let m = MathHelper::arbitrary_axis(normal);
        // Must be orthonormal (M^T * M == I) and right-handed (det == +1)
        assert!((m.transpose() * m).is_identity());
        assert!(MathHelper::is_equal(m.determinant(), 1.0));

        let ocs = MathHelper::transform_3d(
            pt,
            normal,
            CoordinateSystem::World,
            CoordinateSystem::Object,
        );
        let back = MathHelper::transform_3d(
            ocs,
            normal,
            CoordinateSystem::Object,
            CoordinateSystem::World,
        );
        assert!(back.equals_with(pt, 1e-10));
    }
}

#[test]
fn bezier_curves_and_bounding_rectangle() {
    let q = BezierCurveQuadratic::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(5.0, 10.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
    );
    assert_eq!(q.calculate_point(0.0), Vector3::ZERO);
    assert_eq!(q.calculate_point(1.0), Vector3::new(10.0, 0.0, 0.0));
    assert_eq!(q.calculate_point(0.5), Vector3::new(5.0, 5.0, 0.0));

    let c = BezierCurveCubic::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(0.0, 10.0, 0.0),
        Vector3::new(10.0, 10.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
    );
    let poly = c.polygonal_vertexes(10);
    assert_eq!(poly.len(), 11);
    assert_eq!(poly[0], Vector3::ZERO);
    assert_eq!(poly[10], Vector3::new(10.0, 0.0, 0.0));

    let bb = BoundingRectangle::from_points(poly.iter().map(|p| p.to_vector2())).unwrap();
    assert!(MathHelper::is_equal(bb.min.x, 0.0));
    assert!(MathHelper::is_equal(bb.max.x, 10.0));
    assert!(bb.max.y > 7.0);
}

#[test]
fn aci_color_transparency_lineweight() {
    assert_eq!(AciColor::RED.to_rgb(), [255, 0, 0]);
    assert_eq!(AciColor::from_index(3).unwrap().to_rgb(), [0, 255, 0]);
    assert!(AciColor::from_index(0).is_err());
    assert!(AciColor::from_index(256).is_err());

    let tc = AciColor::from_rgb(12, 134, 245);
    assert!(tc.use_true_color);
    let packed = tc.to_true_color();
    let unpacked = AciColor::from_true_color(packed);
    assert_eq!(unpacked.to_rgb(), [12, 134, 245]);

    let tr = Transparency::new(50).unwrap();
    let alpha = tr.to_alpha_value();
    let tr2 = Transparency::from_alpha_value(alpha);
    assert_eq!(tr2.stored_alpha_value(), Some(alpha));
    assert!((tr2.value() - 50).abs() <= 1);
    assert!(Transparency::new(95).is_err());

    let ls_zero = Transparency::from_layer_state_alpha_value(0);
    assert_eq!(ls_zero.value(), 0);
    assert_eq!(ls_zero.stored_alpha_value(), Some(0));

    assert_eq!(Lineweight::from_i16(35), Some(Lineweight::W35));
    assert_eq!(Lineweight::from_i16(-1), Some(Lineweight::ByLayer));
}

#[test]
fn units_and_drawing_time() {
    let mm = UnitHelper::convert(1.0, DrawingUnits::Inches, DrawingUnits::Millimeters);
    assert!(MathHelper::is_equal_with(mm, 25.4, 1e-12));

    let t = UNIX_EPOCH + Duration::from_secs(1_700_000_000);
    let julian = DrawingTime::to_julian_calendar(t);
    let back = DrawingTime::from_julian_calendar(julian).unwrap();
    let diff = back
        .duration_since(t)
        .or_else(|e| Ok::<_, ()>(e.duration()))
        .unwrap();
    assert!(diff < Duration::from_millis(10));

    let dur = Duration::from_secs(3600);
    let days = DrawingTime::to_elapsed_days(dur);
    let dur_back = DrawingTime::editing_time(days).unwrap();
    assert_eq!(dur_back.as_secs(), 3600);
}
