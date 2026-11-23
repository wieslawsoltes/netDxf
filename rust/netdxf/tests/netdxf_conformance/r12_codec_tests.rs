use netdxf::entities::{
    Arc, Circle, Line, Point, Polyline2D, Polyline2DVertex, Polyline3D, Text,
};
use netdxf::io::DxfR12Codec;
use netdxf::{DxfDocument, Vector2, Vector3};

#[test]
fn r12_codec_export_and_import_roundtrip() {
    let mut doc = DxfDocument::new();
    doc.add_entity(Point::new(Vector3::new(1.0, 2.0, 3.0)));
    doc.add_entity(Line::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(10.0, 20.0, 0.0),
    ));
    doc.add_entity(Circle::new(Vector3::new(5.0, 5.0, 0.0), 4.5));
    doc.add_entity(Arc::new(Vector3::new(2.0, 3.0, 0.0), 6.0, 15.0, 135.0));
    doc.add_entity(Text::new("R12 Hello", Vector3::new(1.0, 1.0, 0.0), 2.5));
    doc.add_entity(Polyline2D::new(
        vec![
            Polyline2DVertex::new(0.0, 0.0),
            Polyline2DVertex::with_bulge(10.0, 0.0, 0.5),
            Polyline2DVertex::new(10.0, 10.0),
        ],
        true,
    ));
    doc.add_entity(Polyline3D::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 2.0, 3.0),
            Vector3::new(4.0, 5.0, 6.0),
        ],
        false,
    ));

    let r12_bytes = DxfR12Codec::write_to_bytes(&doc).expect("R12 write");
    let text = String::from_utf8(r12_bytes.clone()).expect("ASCII R12");
    assert!(text.contains("AC1009"));
    assert!(!text.contains("AcDbEntity"), "R12 must not emit subclass markers");

    let loaded = DxfR12Codec::read_from_bytes(&r12_bytes).expect("R12 read");
    assert_eq!(loaded.entities.points().count(), 1);
    assert_eq!(loaded.entities.lines().count(), 1);
    assert_eq!(loaded.entities.circles().count(), 1);
    assert_eq!(loaded.entities.arcs().count(), 1);
    assert_eq!(loaded.entities.texts().count(), 1);
    assert_eq!(loaded.entities.polylines2d().count(), 1);
    assert_eq!(loaded.entities.polylines3d().count(), 1);

    let pl2 = loaded.entities.polylines2d().next().unwrap();
    assert!(pl2.is_closed);
    assert_eq!(pl2.vertexes.len(), 3);
    assert_eq!(pl2.vertexes[1].position, Vector2::new(10.0, 0.0));
    assert!((pl2.vertexes[1].bulge - 0.5).abs() < 1e-12);
}
