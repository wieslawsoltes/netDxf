use netdxf::blocks::Block;
use netdxf::clipping_boundary::ClippingBoundary;
use netdxf::entities::*;
use netdxf::header::DxfVersion;
use netdxf::objects::{Group, MLineStyle};
use netdxf::tables::{ApplicationRegistry, DimensionStyle, Layer, Linetype, TextStyle, UCS};
use netdxf::{
    AciColor, DxfDocument, Lineweight, MathHelper, Matrix3, Transparency, Vector2, Vector3, XData,
    XDataCode, XDataRecord, XDataValue,
};

fn build_rich_document(version: DxfVersion) -> DxfDocument {
    let mut doc = DxfDocument::with_version(version);
    doc.comments.push("Custom rust port test comment".to_string());

    // Custom tables
    let mut layer = Layer::new("Geometry").unwrap();
    layer.color = AciColor::from_rgb(30, 144, 255);
    layer.lineweight = Lineweight::W35;
    layer.transparency = Transparency::new(25).unwrap();
    doc.layers.add(layer);

    doc.linetypes.add(Linetype::dashed());
    doc.text_styles
        .add(TextStyle::new("Notes", "arial.ttf").unwrap());
    doc.dimension_styles.add(DimensionStyle::iso25());
    doc.ucss
        .add(UCS::from_origin_axes("PlanUCS", Vector3::new(10.0, 20.0, 0.0), Vector3::UNIT_X, Vector3::UNIT_Y).unwrap());
    doc.application_registries
        .add(ApplicationRegistry::new("NETDXF_RUST").unwrap());

    // Block with entity + attribute definition
    let mut blk = Block::new("Bolt").unwrap();
    blk.add_entity(Circle::new(Vector3::ZERO, 2.5));
    let mut attdef = AttributeDefinition::new("PART_NO");
    attdef.prompt = "Enter part number".to_string();
    attdef.value = "M10".to_string();
    blk.add_attribute_definition(attdef);
    doc.blocks.add(blk);

    // Entities
    let mut line = Line::new(Vector3::new(0.0, 0.0, 0.0), Vector3::new(100.0, 50.0, 10.0));
    line.common.layer = "Geometry".to_string();
    line.common.color = AciColor::RED;
    let mut xd = XData::new("NETDXF_RUST");
    xd.x_data_record.push(XDataRecord::string("beam-1"));
    xd.x_data_record.push(XDataRecord::real(42.5));
    xd.x_data_record.push(XDataRecord::int16(7));
    xd.x_data_record.push(XDataRecord::int32(123_456));
    xd.x_data_record.push(XDataRecord::new(
        XDataCode::BinaryData,
        XDataValue::Binary(vec![0xDE, 0xAD, 0xBE, 0xEF]),
    ));
    line.common.object.add_xdata(xd);
    doc.add_entity(line);

    doc.add_entity(Point::new(Vector3::new(5.0, 6.0, 7.0)));
    doc.add_entity(Ray::new(Vector3::ZERO, Vector3::new(1.0, 1.0, 0.0)));
    doc.add_entity(XLine::new(Vector3::new(1.0, 2.0, 0.0), Vector3::UNIT_Y));

    let mut circle = Circle::new(Vector3::new(25.0, 25.0, 0.0), 12.5);
    circle.common.layer = "Geometry".to_string();
    doc.add_entity(circle);

    doc.add_entity(Arc::new(Vector3::new(10.0, 10.0, 0.0), 8.0, 30.0, 150.0));

    let mut ellipse = Ellipse::new(Vector3::new(50.0, 50.0, 0.0), 20.0, 10.0);
    ellipse.rotation = 30.0;
    doc.add_entity(ellipse);

    doc.add_entity(Solid::new(
        Vector2::new(0.0, 0.0),
        Vector2::new(10.0, 0.0),
        Vector2::new(0.0, 10.0),
        Vector2::new(10.0, 10.0),
    ));
    doc.add_entity(Trace::new(
        Vector2::new(1.0, 1.0),
        Vector2::new(2.0, 1.0),
        Vector2::new(1.0, 2.0),
        Vector2::new(2.0, 2.0),
    ));
    doc.add_entity(Face3D::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
        Vector3::new(10.0, 10.0, 5.0),
        Vector3::new(0.0, 10.0, 5.0),
    ));

    let mut txt = Text::new("Hello netDxf Rust", Vector3::new(5.0, 5.0, 0.0), 3.5);
    txt.alignment = TextAlignment::MiddleCenter;
    txt.style = "Notes".to_string();
    doc.add_entity(txt);

    let long_mtext = "A".repeat(310) + "\\PSecond Line";
    doc.add_entity(MText::new(long_mtext, Vector3::new(0.0, 20.0, 0.0), 2.5));

    let pl2 = Polyline2D::new(
        vec![
            Polyline2DVertex::new(0.0, 0.0),
            Polyline2DVertex::with_bulge(10.0, 0.0, 0.5),
            Polyline2DVertex::new(10.0, 10.0),
        ],
        true,
    );
    doc.add_entity(pl2);

    doc.add_entity(Polyline3D::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(5.0, 5.0, 5.0),
            Vector3::new(10.0, 0.0, 10.0),
        ],
        false,
    ));

    let spline = Spline::from_control_points(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(10.0, 20.0, 0.0),
            Vector3::new(20.0, -10.0, 0.0),
            Vector3::new(30.0, 10.0, 0.0),
        ],
        3,
        false,
    );
    assert_eq!(spline.polygonal_vertexes(16).len(), 16);
    doc.add_entity(spline);

    let hatch = Hatch::new(
        HatchPattern::solid(),
        vec![HatchBoundaryPath::new(vec![
            HatchBoundaryPathEdge::Polyline {
                vertexes: vec![
                    (Vector2::new(0.0, 0.0), 0.0),
                    (Vector2::new(10.0, 0.0), 0.0),
                    (Vector2::new(10.0, 10.0), 0.0),
                    (Vector2::new(0.0, 10.0), 0.0),
                ],
                is_closed: true,
            },
        ])],
    );
    doc.add_entity(hatch);

    let mut ins = Insert::with_position("Bolt", Vector3::new(30.0, 40.0, 0.0));
    ins.rotation = 45.0;
    ins.attributes.push(Attribute::new("PART_NO", "M10-SS"));
    doc.add_entity(ins);

    doc.add_entity(Dimension::linear(
        Vector2::new(0.0, 0.0),
        Vector2::new(50.0, 0.0),
        10.0,
        0.0,
    ));
    doc.add_entity(Leader::new(vec![
        Vector2::new(0.0, 0.0),
        Vector2::new(10.0, 10.0),
        Vector2::new(20.0, 10.0),
    ]));
    doc.add_entity(MLine::new(vec![Vector2::ZERO, Vector2::new(25.0, 0.0)]));
    doc.add_entity(Mesh::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(1.0, 1.0, 0.0),
            Vector3::new(0.0, 1.0, 0.0),
        ],
        vec![vec![0, 1, 2, 3]],
    ));
    doc.add_entity(PolyfaceMesh::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(0.0, 1.0, 0.0),
        ],
        vec![PolyfaceMeshFace::new(vec![1, 2, 3])],
    ));
    doc.add_entity(PolygonMesh::new(
        2,
        2,
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(0.0, 1.0, 0.0),
            Vector3::new(1.0, 1.0, 1.0),
        ],
    ));
    doc.add_entity(Viewport::new());
    doc.add_entity(Tolerance::new("{\\Fgdt;j}%%v0.05", Vector3::new(1.0, 2.0, 0.0)));
    doc.add_entity(Wipeout::new(ClippingBoundary::rectangular(
        Vector2::ZERO,
        Vector2::new(5.0, 5.0),
    )));

    // Objects
    let mut grp = Group::new("MyGroup").unwrap();
    grp.description = "Sample group".to_string();
    doc.groups.add(grp);
    doc.mline_styles.add(MLineStyle::new("Wall").unwrap());
    doc.thumbnail_image = vec![0x42, 0x4D, 0x10, 0x20, 0x30, 0x40];

    doc
}

fn assert_roundtrip(version: DxfVersion, is_binary: bool) {
    let mut doc = build_rich_document(version);
    let bytes = doc.save_to_bytes(is_binary).expect("save failed");

    let (detected_ver, detected_bin) =
        DxfDocument::check_dxf_stream_version(std::io::Cursor::new(&bytes)).unwrap();
    assert_eq!(detected_ver, version);
    assert_eq!(detected_bin, is_binary);

    let loaded = DxfDocument::load_from_bytes(&bytes).expect("load failed");
    assert_eq!(loaded.drawing_variables.acad_ver, version);
    assert_eq!(loaded.entities.len(), doc.entities.len());
    assert_eq!(loaded.entities.lines().count(), 1);
    assert_eq!(loaded.entities.circles().count(), 1);
    assert_eq!(loaded.entities.arcs().count(), 1);
    assert_eq!(loaded.entities.ellipses().count(), 1);
    assert_eq!(loaded.entities.polylines2d().count(), 1);
    assert_eq!(loaded.entities.polylines3d().count(), 1);
    assert_eq!(loaded.entities.splines().count(), 1);
    assert_eq!(loaded.entities.hatches().count(), 1);
    assert_eq!(loaded.entities.inserts().count(), 1);
    assert_eq!(loaded.entities.dimensions().count(), 1);
    assert_eq!(loaded.entities.meshes().count(), 1);
    assert_eq!(loaded.entities.polyface_meshes().count(), 1);
    assert_eq!(loaded.entities.polygon_meshes().count(), 1);

    // Check line + XData round-trip
    let line = loaded.entities.lines().next().unwrap();
    assert_eq!(line.common.layer, "Geometry");
    assert_eq!(line.common.color, AciColor::RED);
    assert!(line.end_point.equals_with(Vector3::new(100.0, 50.0, 10.0), 1e-9));
    let xd = line.common.object.get_xdata("NETDXF_RUST").expect("missing XData");
    assert_eq!(xd.x_data_record.len(), 5);
    assert_eq!(xd.x_data_record[0].value, XDataValue::String("beam-1".into()));
    assert_eq!(xd.x_data_record[4].value, XDataValue::Binary(vec![0xDE, 0xAD, 0xBE, 0xEF]));

    // Check layer TrueColor (R2004+) or nearest ACI fallback (R2000) + lineweight
    let geo_layer = loaded.layers.get("geometry").expect("case-insensitive layer lookup");
    if loaded.drawing_variables.acad_ver >= DxfVersion::AutoCad2004 {
        assert_eq!(geo_layer.color.to_rgb(), [30, 144, 255]);
    } else {
        assert!(!geo_layer.color.use_true_color);
    }
    assert_eq!(geo_layer.lineweight, Lineweight::W35);

    // Check block + ATTDEF + INSERT ATTRIB
    let blk = loaded.blocks.get("bolt").expect("missing Bolt block");
    assert_eq!(blk.entities.len(), 1);
    assert_eq!(blk.attribute_definitions.len(), 1);
    let ins = loaded.entities.inserts().next().unwrap();
    assert_eq!(ins.block_name, "Bolt");
    assert_eq!(ins.attributes.len(), 1);
    assert_eq!(ins.attributes[0].value, "M10-SS");

    // Check long MText chunking round-trip
    let mt = loaded.entities.mtexts().next().unwrap();
    assert!(mt.value.len() > 310);
    assert!(mt.plain_text().contains('\n'));

    // Check thumbnail
    assert_eq!(loaded.thumbnail_image, vec![0x42, 0x4D, 0x10, 0x20, 0x30, 0x40]);
}

#[test]
fn roundtrip_ascii_r2000_r2007_r2018() {
    assert_roundtrip(DxfVersion::AutoCad2000, false);
    assert_roundtrip(DxfVersion::AutoCad2007, false);
    assert_roundtrip(DxfVersion::AutoCad2018, false);
}

#[test]
fn roundtrip_binary_r2000_r2018() {
    assert_roundtrip(DxfVersion::AutoCad2000, true);
    assert_roundtrip(DxfVersion::AutoCad2018, true);
}

#[test]
fn entity_transform_by_works() {
    let mut line = Line::new(Vector3::new(1.0, 0.0, 0.0), Vector3::new(2.0, 0.0, 0.0));
    line.transform_by(Matrix3::rotation_z(MathHelper::HALF_PI), Vector3::new(10.0, 0.0, 0.0));
    assert!(line.start_point.equals_with(Vector3::new(10.0, 1.0, 0.0), 1e-12));
    assert!(line.end_point.equals_with(Vector3::new(10.0, 2.0, 0.0), 1e-12));
}
