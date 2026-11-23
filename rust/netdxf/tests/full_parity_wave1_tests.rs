use netdxf::entities::{
    DatumReferenceValue, Ellipse, Face3DEdgeFlags, Hatch, HatchPattern, Helix, HelixConstraint,
    MText, MTextColumnStorage, Mesh, MeshEdge, PolyfaceMesh, PolyfaceMeshFace, PolygonMesh,
    Polyline2D, Polyline2DVertex, Polyline3D, PolylineSmoothType, Tolerance,
    ToleranceEntry, ToleranceGeometricSymbol, ToleranceMaterialCondition, ToleranceValue,
};
use netdxf::gte::{
    BSplineCurve, BSplineCurveFit, BasisFunction, BasisFunctionInput, GMatrix, GVector,
    ParametricCurve,
};
use netdxf::header::{DxfVersion, HeaderVariable, HeaderVariableValue};
use netdxf::io::{
    DxfDuplicateRecordCloning, DxfRawDocument, DxfRawObjectStore, DxfRawObjectStoreOptions,
    DxfTag,
};
use netdxf::objects::{DxfSpatialFilter, LayerState, PlotSettings, PlotType};
use netdxf::tables::{Layer, UCS};
use netdxf::{
    AciColor, DxfDimensionPictureOptions, DxfDocument, Matrix4, Vector2, Vector3, Vector4,
};

#[test]
fn matrix4_and_gte_linear_algebra_parity() {
    let t = Matrix4::translation(Vector3::new(3.0, 4.0, 5.0));
    let r = Matrix4::rotation_z(std::f64::consts::FRAC_PI_2);
    let m = t * r;
    let p = m * Vector4::new(1.0, 0.0, 0.0, 1.0);
    assert!((p.x - 3.0).abs() < 1e-12);
    assert!((p.y - 5.0).abs() < 1e-12);
    assert!((p.z - 5.0).abs() < 1e-12);
    let inv = m.inverse().expect("invertible");
    let back = inv * p;
    assert!((back.x - 1.0).abs() < 1e-12);
    assert!(back.y.abs() < 1e-12);

    let v = GVector::from_vec(vec![1.0, 2.0, 3.0]);
    let lifted = v.h_lift(1.0);
    assert_eq!(lifted.size(), 4);
    assert_eq!(lifted.h_project().unwrap(), v);
    assert_eq!(v.lift(1, 9.0).project(1), v);

    let a = GMatrix::from_elements(2, 2, vec![4.0, 7.0, 2.0, 6.0]);
    assert!((a.determinant() - 10.0).abs() < 1e-12);
    let a_inv = a.inverse().expect("2x2 invertible");
    let prod = GMatrix::multiply_ab(&a, &a_inv);
    assert!((prod[(0, 0)] - 1.0).abs() < 1e-12);
    assert!((prod[(1, 1)] - 1.0).abs() < 1e-12);
    assert!(prod[(0, 1)].abs() < 1e-12);
}

#[test]
fn gte_parametric_curve_and_bspline_fit_parity() {
    let input = BasisFunctionInput::open_uniform(4, 3);
    let bf = BasisFunction::new(&input);
    assert!(bf.is_open());
    assert!(bf.is_uniform());
    assert_eq!(bf.num_knots(), 8);

    let curve = BSplineCurve::new(
        &input,
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(2.0, 0.0, 0.0),
            Vector3::new(3.0, 0.0, 0.0),
        ],
    );
    let total_len = curve.get_total_length();
    assert!((total_len - 3.0).abs() < 1e-6);
    let t_half = curve.get_time(1.5);
    let pt_half = curve.get_position(t_half);
    assert!((pt_half.x - 1.5).abs() < 1e-5);

    let (pts, ts) = curve.subdivide_by_length(4);
    assert_eq!(pts.len(), 4);
    assert_eq!(ts.len(), 4);
    assert!((pts[3].x - 3.0).abs() < 1e-5);

    let samples: Vec<Vector3> = (0..=10)
        .map(|i| Vector3::new(i as f64, (i as f64 * 0.2).sin(), 0.0))
        .collect();
    let fit = BSplineCurveFit::fit(3, 5, &samples).expect("fit succeeds");
    assert_eq!(fit.num_samples(), 11);
    assert_eq!(fit.num_controls(), 5);
    assert!((fit.get_position(0.0).x - 0.0).abs() < 0.2);
}

#[test]
fn curves_and_meshes_full_parity() {
    let e = Ellipse::new(Vector3::new(1.0, 2.0, 0.0), 10.0, 4.0);
    let jet = e.evaluate_derivatives(0.0, 2);
    assert_eq!(jet.len(), 3);
    assert!((jet[0].x - 6.0).abs() < 1e-12);
    assert!((jet[1].y - 2.0).abs() < 1e-12);
    let poly = e.to_polyline2d(16);
    assert_eq!(poly.vertexes.len(), 16);
    assert!(Ellipse::convert_from(&poly).is_some());

    let h = Helix::create(
        Vector3::ZERO,
        Vector3::new(2.0, 0.0, 0.0),
        Vector3::UNIT_Z,
        2.0,
        5.0,
        true,
        HelixConstraint::Turns,
    );
    let end_pt = h.evaluate_definition(1.0);
    assert!((end_pt.z - 10.0).abs() < 1e-9);
    assert!(h.get_approximation_error_bound(32) > 0.0);
    let h_spline = h.to_spline(24);
    assert!(h_spline.evaluate_derivatives(0.5, 2).is_some());

    let p2 = Polyline2D::new(
        vec![
            Polyline2DVertex {
                position: Vector2::new(0.0, 0.0),
                start_width: 0.0,
                end_width: 0.0,
                bulge: 1.0,
            },
            Polyline2DVertex::from_position(Vector2::new(10.0, 0.0)),
        ],
        false,
    );
    let exploded = p2.explode();
    assert_eq!(exploded.len(), 1);
    assert!(matches!(exploded[0], netdxf::entities::EntityObject::Arc(_)));
    assert!(p2.polygonal_vertexes(8).len() >= 9);

    let mut p3 = Polyline3D::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 2.0, 0.0),
            Vector3::new(2.0, 0.0, 1.0),
            Vector3::new(3.0, 1.0, 2.0),
        ],
        false,
    );
    p3.smooth_type = PolylineSmoothType::Cubic;
    assert!(!p3.explode().is_empty());

    let mut mesh = Mesh::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(1.0, 1.0, 0.0),
            Vector3::new(0.0, 1.0, 0.0),
        ],
        vec![vec![0, 1, 2, 3]],
    );
    mesh.insert_edge(
        0,
        MeshEdge {
            start_vertex_index: 0,
            end_vertex_index: 1,
            crease: 0.0,
        },
    );
    assert!(mesh.blend_crease(0, 2.5));
    assert_eq!(mesh.explode_control_mesh().len(), 1);
    mesh.subdivision_level = 1;
    assert_eq!(mesh.explode().len(), 4);

    let pmesh = PolygonMesh::new(
        2,
        2,
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(0.0, 1.0, 0.0),
            Vector3::new(1.0, 1.0, 1.0),
        ],
    );
    assert_eq!(pmesh.explode().len(), 1);

    let mut pf = PolyfaceMesh::new(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 0.0, 0.0),
            Vector3::new(1.0, 1.0, 0.0),
        ],
        vec![PolyfaceMeshFace::new(vec![1, 2, 3])],
    );
    assert!(pf.set_face_edge_visibility(0, 1, false));
    let pf_faces = pf.explode();
    assert_eq!(pf_faces.len(), 1);
    if let netdxf::entities::EntityObject::Face3D(f) = &pf_faces[0] {
        assert!(f.edge_flags.contains(Face3DEdgeFlags::SECOND));
    } else {
        panic!("expected Face3D");
    }
}

#[test]
fn document_tables_tolerance_hatch_and_raw_store_parity() {
    let mut doc = DxfDocument::new();
    doc.drawing_variables.add_custom_variable(HeaderVariable::new(
        "$CUSTOM1",
        1,
        HeaderVariableValue::String("hello".to_string()),
    ));
    assert!(doc.drawing_variables.contains_custom_variable("$CUSTOM1"));
    assert!(doc.viewport().is_some());
    doc.assign_all_handles();
    let layer_handle = doc.layers.get("0").unwrap().table.object.handle.clone().unwrap();
    assert!(doc.get_object_by_handle(&layer_handle).is_some());
    let _report = doc.analyze_version_compatibility(DxfVersion::AutoCad2000);

    let ucs = UCS::from_x_axis_and_point_on_xy_plane(
        "MyUcs",
        Vector3::ZERO,
        Vector3::new(0.0, 1.0, 0.0),
        Vector3::new(-1.0, 0.0, 0.0),
    )
    .unwrap();
    assert!((ucs.get_transformation().determinant() - 1.0).abs() < 1e-12);

    let mut ls = LayerState::new("Plan").unwrap();
    let mut l = Layer::new("Walls").unwrap();
    l.color = AciColor::RED;
    ls.capture_from_layers([&l]);
    let las = ls.to_las_text();
    let parsed_ls = LayerState::from_las_text(&las).unwrap();
    assert!(parsed_ls.properties.contains_key("Walls"));

    let entry = ToleranceEntry {
        geometric_symbol: ToleranceGeometricSymbol::Position,
        tolerance1: Some(ToleranceValue {
            show_diameter_symbol: true,
            value: "0.05".to_string(),
            material_condition: ToleranceMaterialCondition::Maximum,
        }),
        tolerance2: None,
        datum1: Some(DatumReferenceValue {
            value: "A".to_string(),
            material_condition: ToleranceMaterialCondition::None,
        }),
        datum2: None,
        datum3: None,
    };
    let tol = Tolerance::from_entry(entry.clone(), Vector3::ZERO);
    let rep = tol.to_string_representation();
    let mut reparsed = Tolerance::new("", Vector3::ZERO);
    reparsed.parse_string_representation(&rep);
    assert_eq!(reparsed.entry1, Some(entry));

    let mut mt = MText::new("Hello", Vector3::ZERO, 2.5);
    mt.convert_to_embedded_columns();
    assert_eq!(mt.columns.as_ref().unwrap().storage, MTextColumnStorage::Embedded);

    let tmp_dir = tempfile::tempdir().unwrap();
    let pat_path = tmp_dir.path().join("test.pat");
    HatchPattern::net().save(&pat_path).unwrap();
    let loaded_pat = HatchPattern::load(&pat_path, "NET").unwrap();
    assert_eq!(loaded_pat.line_definitions.len(), 2);

    let hatch = Hatch::new(HatchPattern::solid(), Vec::new());
    let _ = hatch.create_boundary(&[]);

    let dxf_path = tmp_dir.path().join("dim_pic.dxf");
    doc.save_with_dimension_pictures(&dxf_path, false, &DxfDimensionPictureOptions::default())
        .unwrap();
    let raw = DxfRawDocument::load(&dxf_path).unwrap();
    assert!(raw.has_original_bytes());
    assert!(raw.find_section("HEADER").is_some());

    let mut store = DxfRawObjectStore::new(DxfRawObjectStoreOptions::default());
    let mut tx = store.begin_transaction();
    let root = tx.ensure_root_dictionary();
    let sub = tx.create_dictionary(root, DxfDuplicateRecordCloning::KeepExisting);
    let xrec = tx.create_xrecord(sub, vec![DxfTag::new(1, netdxf::io::CodeValue::String("v".into()))]);
    assert!(tx.set_dictionary_entry(sub, "DATA", xrec));
    let cloned = tx.clone_dictionary_tree(sub, root).unwrap();
    assert_ne!(cloned, sub);
    assert_eq!(tx.delete_owned_tree(cloned), 2);
    tx.commit(&mut store);
    assert!(store.get(root).is_some());

    let mut sf = DxfSpatialFilter::new();
    sf.set_boundary([Vector2::ZERO, Vector2::new(10.0, 10.0)]);
    assert_eq!(sf.boundary().len(), 2);

    let ps = PlotSettings {
        plot_type: PlotType::Window,
        ..PlotSettings::default()
    };
    assert!((ps.print_scale() - 1.0).abs() < 1e-12);
}
