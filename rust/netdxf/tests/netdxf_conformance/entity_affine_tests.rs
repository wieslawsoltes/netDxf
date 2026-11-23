use netdxf::entities::{Circle, Ellipse, Line, Point, Polyline2D, Polyline2DVertex, Polyline3D};
use netdxf::{MathHelper, Matrix3, Vector2, Vector3};

#[test]
fn entity_affine_transforms_conformance() {
    let rot = Matrix3::rotation_z(MathHelper::HALF_PI);
    let translate = Vector3::new(10.0, -5.0, 2.0);

    let mut pt = Point::new(Vector3::new(3.0, 4.0, 1.0));
    pt.transform_by(rot, translate);
    assert!((pt.position - Vector3::new(6.0, -2.0, 3.0)).modulus() < 1e-12);

    let mut line = Line::new(Vector3::new(1.0, 0.0, 0.0), Vector3::new(2.0, 0.0, 0.0));
    line.transform_by(Matrix3::scale(2.0, 3.0, 1.0), Vector3::new(1.0, 2.0, 3.0));
    assert!((line.start_point - Vector3::new(3.0, 2.0, 3.0)).modulus() < 1e-12);
    assert!((line.end_point - Vector3::new(5.0, 2.0, 3.0)).modulus() < 1e-12);

    let mut circle = Circle::new(Vector3::new(1.0, 2.0, 0.0), 5.0);
    circle.transform_by(Matrix3::scale(2.0, 2.0, 2.0), Vector3::new(10.0, 0.0, 0.0));
    assert!((circle.center - Vector3::new(12.0, 4.0, 0.0)).modulus() < 1e-12);
    assert!((circle.radius - 10.0).abs() < 1e-12);

    let mut ellipse = Ellipse::new(Vector3::ZERO, 10.0, 4.0);
    ellipse.transform_by(Matrix3::scale(2.0, 2.0, 1.0), Vector3::new(5.0, 5.0, 0.0));
    assert!((ellipse.center - Vector3::new(5.0, 5.0, 0.0)).modulus() < 1e-12);
    assert!((ellipse.major_axis - 20.0).abs() < 1e-12);
    assert!((ellipse.minor_axis - 8.0).abs() < 1e-12);

    let mut pl2 = Polyline2D::new(
        vec![
            Polyline2DVertex::new(0.0, 0.0),
            Polyline2DVertex::with_bulge(10.0, 0.0, 0.5),
            Polyline2DVertex::new(10.0, 10.0),
        ],
        false,
    );
    pl2.reverse();
    assert_eq!(pl2.vertexes[0].position, Vector2::new(10.0, 10.0));
    assert_eq!(pl2.vertexes[2].position, Vector2::new(0.0, 0.0));
    assert!((pl2.vertexes[0].bulge - (-0.5)).abs() < 1e-12);
    assert!(pl2.vertexes[1].bulge.abs() < 1e-12);

    let mut pl3 = Polyline3D::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 2.0, 3.0),
            Vector3::new(4.0, 5.0, 6.0),
        ],
        false,
    );
    pl3.reverse();
    assert_eq!(pl3.vertexes[0], Vector3::new(4.0, 5.0, 6.0));
    assert_eq!(pl3.vertexes[2], Vector3::ZERO);
}
