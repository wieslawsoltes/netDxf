use netdxf::blocks::Block;
use netdxf::entities::{
    Arc, Circle, Dimension, DimensionBlock, Ellipse, EntityObject, Face3D, Face3DEdgeFlags, Hatch,
    HatchBoundaryPath, HatchBoundaryPathEdge, HatchPattern, Insert, Line, MText, Point, Polyline2D,
    Polyline2DVertex, Polyline3D, Spline,
};
use netdxf::header::DxfVersion;
use netdxf::tables::{ApplicationRegistry, DimensionStyle};
use netdxf::{
    DxfDocument, MathHelper, Matrix3, Vector2, Vector3, XData, XDataRecord, XDataValue,
};

const SUPPORTED_VERSIONS: [DxfVersion; 6] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
    DxfVersion::AutoCad2013,
    DxfVersion::AutoCad2018,
];

fn make_rational_spline(degree: i16) -> Spline {
    let d = degree as usize;
    let count = d + 4;
    let controls: Vec<Vector3> = (0..count)
        .map(|i| {
            Vector3::new(
                i as f64 - 2.0,
                ((i * 7) % 11) as f64 - 4.0,
                (i % 3) as f64 - 1.0,
            )
        })
        .collect();
    let weights: Vec<f64> = (0..count).map(|i| 1.0 + (i % 4) as f64 * 0.25).collect();
    let knot_count = count + d + 1;
    let knots: Vec<f64> = (0..knot_count)
        .map(|i| ((i as f64 - d as f64) / 4.0).clamp(0.0, 1.0))
        .collect();
    let mut s = Spline::from_control_points(controls, degree, false);
    s.weights = weights;
    s.knots = knots;
    s
}

fn make_rational_bezier(degree: i16) -> Spline {
    let d = degree as usize;
    let count = d + 1;
    let controls: Vec<Vector3> = (0..count)
        .map(|i| Vector3::new(i as f64, ((i * 5) % 7) as f64 - 2.0, (i % 3) as f64))
        .collect();
    let weights: Vec<f64> = (0..count).map(|i| 1.0 + (i % 3) as f64 * 0.5).collect();
    let mut s = Spline::from_control_points(controls, degree, false);
    s.weights = weights;
    s
}

#[test]
fn arc_angle_fidelity_and_wire_roundtrip() {
    let pairs = [
        (1e-9, 2e-9),
        (0.25, 90.0),
        (30.0, 210.0),
        (90.0, 180.0),
        (359.5, 0.5),
    ];
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            for (start, end) in pairs {
                let mut arc = Arc::new(Vector3::new(1.0, 2.0, 3.0), 4.0, start, end);
                arc.thickness = 2.0;
                doc.add_entity(arc);
            }
            let bytes = doc.save_to_bytes(binary).expect("save arc angles");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load arc angles");
            let arcs: Vec<&Arc> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Arc(a) => Some(a.as_ref()),
                    _ => None,
                })
                .collect();
            assert_eq!(arcs.len(), pairs.len());
            for (arc, (start, end)) in arcs.iter().zip(pairs.iter()) {
                assert!((arc.start_angle - start).abs() < 1e-11);
                assert!((arc.end_angle - end).abs() < 1e-11);
                assert!((arc.radius - 4.0).abs() < 1e-12);
                assert!((arc.thickness - 2.0).abs() < 1e-12);
                assert!((arc.center - Vector3::new(1.0, 2.0, 3.0)).modulus() < 1e-11);
            }
        }
    }
}

#[test]
fn face3d_edge_visibility_all_16_masks() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            for mask in 0i16..16 {
                let mut face = Face3D::new(
                    Vector3::new(1.0, 2.0, 3.0),
                    Vector3::new(4.0, 5.0, 6.0),
                    Vector3::new(7.0, 8.0, 9.0),
                    Vector3::new(10.0, 11.0, 12.0),
                );
                face.edge_flags = Face3DEdgeFlags::from_bits_truncate(mask);
                doc.add_entity(face);
            }
            let bytes = doc.save_to_bytes(binary).expect("save face3d masks");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load face3d masks");
            let faces: Vec<&Face3D> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Face3D(f) => Some(f.as_ref()),
                    _ => None,
                })
                .collect();
            assert_eq!(faces.len(), 16);
            for (mask, face) in (0i16..16).zip(faces.iter()) {
                assert_eq!(face.edge_flags.bits(), mask);
            }
        }
    }
}

#[test]
fn mtext_unicode_chunk_budget_and_roundtrip() {
    let mut samples: Vec<String> = Vec::new();
    for &count in &[0usize, 1, 248, 249, 250, 251, 499, 500, 501] {
        samples.push("a".repeat(count));
    }
    for &prefix in &[0usize, 1, 246, 247, 248, 249, 250] {
        samples.push(format!("{}{}{}", "x".repeat(prefix), "\u{1F680}", "y".repeat(503)));
    }
    samples.push("Zażółć gęślą jaźń 東京 \u{1F680} e\u{0301} ".repeat(20));
    samples.push("\u{6771}".repeat(333));
    samples.push(format!("{}\\P{{\\C1;red}}\\S1/2;end", "a".repeat(248)));

    for &version in &[
        DxfVersion::AutoCad2007,
        DxfVersion::AutoCad2010,
        DxfVersion::AutoCad2013,
        DxfVersion::AutoCad2018,
    ] {
        for &binary in &[false, true] {
            for sample in &samples {
                let mut doc = DxfDocument::with_version(version);
                doc.application_registries
                    .add(ApplicationRegistry::new("CHUNK_APP").unwrap());
                let mut mt = MText::new(sample.clone(), Vector3::new(2.0, -3.0, 4.0), 2.5);
                mt.rectangle_width = 40.0;
                mt.rotation = 23.0;
                let mut xd = XData::new("CHUNK_APP");
                xd.x_data_record.push(XDataRecord::string("tail"));
                mt.common.object.add_xdata(xd);
                doc.add_entity(mt);
                doc.add_entity(Line::new(
                    Vector3::new(8.0, 9.0, 10.0),
                    Vector3::new(11.0, 12.0, 13.0),
                ));

                let bytes = doc.save_to_bytes(binary).expect("save unicode MTEXT");
                if !binary {
                    let text = String::from_utf8(bytes.clone()).expect("UTF-8 DXF");
                    let lines: Vec<&str> = text.lines().collect();
                    let mut in_mtext = false;
                    let mut i = 0;
                    while i + 1 < lines.len() {
                        let code = lines[i].trim();
                        let val = lines[i + 1];
                        if code == "0" {
                            in_mtext = val.trim() == "MTEXT";
                        } else if in_mtext && (code == "1" || code == "3") {
                            assert!(
                                val.len() <= 250,
                                "MTEXT chunk exceeded 250 UTF-8 bytes: {} bytes",
                                val.len()
                            );
                        }
                        i += 2;
                    }
                }

                let loaded = DxfDocument::load_from_bytes(&bytes).expect("load unicode MTEXT");
                let loaded_mt = loaded
                    .entities
                    .iter()
                    .find_map(|e| match e {
                        EntityObject::MText(m) => Some(m.as_ref()),
                        _ => None,
                    })
                    .unwrap();
                assert_eq!(&loaded_mt.value, sample);
                assert_eq!(
                    loaded_mt
                        .common
                        .object
                        .x_data
                        .get("CHUNK_APP")
                        .unwrap()
                        .x_data_record[0]
                        .value,
                    XDataValue::String("tail".into())
                );
                let line_count = loaded
                    .entities
                    .iter()
                    .filter(|e| matches!(e, EntityObject::Line(_)))
                    .count();
                assert_eq!(line_count, 1);
            }
        }
    }
}

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

#[test]
fn spline_knot_insertion_split_trim_elevate_reverse_conformance() {
    for degree in 1i16..=6 {
        let source = make_rational_spline(degree);
        let orig_pts = source.polygonal_vertexes(33);

        // 1. Insert knot (single & multiple)
        let mut refined = source.clone();
        assert!(refined.insert_knot(0.125, 1));
        assert_eq!(refined.control_points.len(), source.control_points.len() + 1);
        assert_eq!(refined.knots.len(), source.knots.len() + 1);
        let ref_pts = refined.polygonal_vertexes(33);
        for (a, b) in orig_pts.iter().zip(ref_pts.iter()) {
            assert!((*a - *b).modulus() < 1e-11, "degree={degree} insert_knot drift");
        }
        let mut bad = source.clone();
        assert!(!bad.insert_knot(f64::NAN, 1));
        assert!(!bad.insert_knot(-0.5, 1));
        assert!(!bad.insert_knot(1.5, 1));
        assert!(!bad.insert_knot(0.125, 0));

        // 2. Split at interior parameter
        let (left, right) = source.split(0.375).expect("split spline");
        assert_eq!(left.degree, degree);
        assert_eq!(right.degree, degree);
        let mid_expected = source.evaluate(0.375).expect("eval mid");
        let left_end = left.evaluate(0.375).expect("eval left end");
        let right_start = right.evaluate(0.375).expect("eval right start");
        assert!((mid_expected - left_end).modulus() < 1e-11);
        assert!((mid_expected - right_start).modulus() < 1e-11);
        assert!(source.split(0.0).is_none());
        assert!(source.split(1.0).is_none());
        assert!(source.split(f64::INFINITY).is_none());

        // 3. Trim to [0.125, 0.875] and boundary intervals [0.0, 0.5], [0.5, 1.0]
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

        // 4. Degree elevation on rational Bezier segment
        let bez = make_rational_bezier(degree);
        let bez_pts = bez.polygonal_vertexes(33);
        let mut elevated = bez.clone();
        assert!(elevated.elevate_degree());
        assert_eq!(elevated.degree, degree + 1);
        let el_pts = elevated.polygonal_vertexes(33);
        for (a, b) in bez_pts.iter().zip(el_pts.iter()) {
            assert!((*a - *b).modulus() < 1e-10, "degree={degree} elevate drift");
        }

        // 5. Reverse
        let mut rev = source.clone();
        rev.reverse();
        let rev_pts = rev.polygonal_vertexes(33);
        for (a, b) in orig_pts.iter().zip(rev_pts.iter().rev()) {
            assert!((*a - *b).modulus() < 1e-11, "degree={degree} reverse drift");
        }
    }

    // Wire roundtrip of refined + split + trimmed + elevated splines across all versions
    let base = make_rational_spline(3);
    let mut refined = base.clone();
    refined.insert_knot(0.25, 2);
    let (s_left, s_right) = base.split(0.5).unwrap();
    let trimmed = base.trim(0.125, 0.875).unwrap();
    let mut elevated = make_rational_bezier(3);
    elevated.elevate_degree();

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            doc.add_entity(base.clone());
            doc.add_entity(refined.clone());
            doc.add_entity(s_left.clone());
            doc.add_entity(s_right.clone());
            doc.add_entity(trimmed.clone());
            doc.add_entity(elevated.clone());

            let bytes = doc.save_to_bytes(binary).expect("save splines");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load splines");
            let splines: Vec<&Spline> = loaded
                .entities
                .iter()
                .filter_map(|e| match e {
                    EntityObject::Spline(s) => Some(s.as_ref()),
                    _ => None,
                })
                .collect();
            assert_eq!(splines.len(), 6);
            assert_eq!(splines[5].degree, 4);
            let p0 = splines[0].evaluate(0.4).unwrap();
            let p1 = splines[1].evaluate(0.4).unwrap();
            assert!((p0 - p1).modulus() < 1e-10);
        }
    }
}

#[test]
fn hatch_and_dimension_block_synthesis_conformance() {
    let bp = HatchBoundaryPath::new(vec![HatchBoundaryPathEdge::Polyline {
        vertexes: vec![
            (Vector2::new(0.0, 0.0), 0.0),
            (Vector2::new(10.0, 0.0), 0.0),
            (Vector2::new(10.0, 10.0), 0.0),
            (Vector2::new(0.0, 10.0), 0.0),
        ],
        is_closed: true,
    }]);
    let hatch = Hatch::new(HatchPattern::solid(), vec![bp]);
    let empty_hatch = Hatch::new(HatchPattern::new("ANSI31", "ANSI Iron"), Vec::new());

    let style = DimensionStyle::default_style();
    let dim_lin = Dimension::linear(Vector2::new(0.0, 0.0), Vector2::new(100.0, 0.0), 15.0, 0.0);
    let blk_lin = DimensionBlock::build(&dim_lin, &style, "*D1");
    assert!(!blk_lin.entities.is_empty());

    let dim_rad = Dimension::radial(Vector2::new(0.0, 0.0), Vector2::new(25.0, 0.0));
    let blk_rad = DimensionBlock::build(&dim_rad, &style, "*D2");
    assert!(!blk_rad.entities.is_empty());

    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    doc.blocks.add(blk_lin);
    doc.blocks.add(blk_rad);
    let mut grp_blk = Block::new("UNIT_BLK").unwrap();
    grp_blk.add_entity(Line::new(Vector3::ZERO, Vector3::UNIT_X));
    doc.blocks.add(grp_blk);
    doc.add_entity(Insert::new("UNIT_BLK"));
    doc.add_entity(hatch);
    doc.add_entity(empty_hatch);
    doc.add_entity(dim_lin);
    doc.add_entity(dim_rad);

    for &binary in &[false, true] {
        let bytes = doc.save_to_bytes(binary).expect("save hatch/dim");
        let loaded = DxfDocument::load_from_bytes(&bytes).expect("load hatch/dim");
        let hatches = loaded
            .entities
            .iter()
            .filter(|e| matches!(e, EntityObject::Hatch(_)))
            .count();
        let dims = loaded
            .entities
            .iter()
            .filter(|e| matches!(e, EntityObject::Dimension(_)))
            .count();
        assert_eq!(hatches, 2);
        assert_eq!(dims, 2);
    }
}
