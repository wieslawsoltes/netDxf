use std::path::PathBuf;

use netdxf::entities::*;
use netdxf::gte::*;
use netdxf::header::DxfVersion;
use netdxf::objects::*;
use netdxf::tables::*;
use netdxf::{DxfDocument, MathHelper, Vector2, Vector3};

#[test]
fn gte_linear_algebra_integration_and_roots() {
    let mut a = GMatrix::zero(2, 2);
    a[(0, 0)] = 4.0;
    a[(0, 1)] = 7.0;
    a[(1, 0)] = 2.0;
    a[(1, 1)] = 6.0;
    let b = GVector::from_vec(vec![1.0, 2.0]);
    let x = GaussianElimination::solve(&a, &b).unwrap();
    let ax = a.mul_vec(&x);
    assert!(MathHelper::is_equal_with(ax[0], 1.0, 1e-12));
    assert!(MathHelper::is_equal_with(ax[1], 2.0, 1e-12));

    let inv = GaussianElimination::inverse(&a).unwrap();
    let prod = a.mul_mat(&inv);
    assert!(MathHelper::is_equal_with(prod[(0, 0)], 1.0, 1e-12));
    assert!(MathHelper::is_equal_with(prod[(1, 1)], 1.0, 1e-12));

    // Integral of sin(x) from 0 to PI is 2.0
    let area = Integration::romberg(8, 0.0, MathHelper::PI, f64::sin);
    assert!(MathHelper::is_equal_with(area, 2.0, 1e-10));
    let area_s = Integration::simpson(100, 0.0, MathHelper::PI, f64::sin);
    assert!(MathHelper::is_equal_with(area_s, 2.0, 1e-7));

    // Root of x^2 - 2 = 0 in [1, 2] is sqrt(2)
    let root = RootBisection::find(|x| x * x - 2.0, 1.0, 2.0, 100).unwrap();
    assert!(MathHelper::is_equal_with(root, 2.0_f64.sqrt(), 1e-12));
}

#[test]
fn gte_bspline_curve_and_surface_fitting() {
    let samples: Vec<Vector3> = (0..=20)
        .map(|i| {
            let t = i as f64 / 20.0;
            Vector3::new(t * 10.0, (t * MathHelper::PI).sin() * 5.0, t * 2.0)
        })
        .collect();
    let fit = BSplineCurveFit::fit(3, 8, &samples).expect("curve fit failed");
    let mid = fit.position(0.5);
    assert!(mid.equals_with(Vector3::new(5.0, 5.0, 1.0), 0.05));

    // Tensor-product surface z = x + 2*y
    let nu = 6;
    let nv = 6;
    let mut grid = Vec::with_capacity(nu * nv);
    for iu in 0..nu {
        let u = iu as f64 / (nu - 1) as f64;
        for iv in 0..nv {
            let v = iv as f64 / (nv - 1) as f64;
            grid.push(Vector3::new(u * 4.0, v * 4.0, u * 4.0 + 2.0 * v * 4.0));
        }
    }
    let sfit = BSplineSurfaceFit::fit(2, 4, 2, 4, nu, nv, &grid).expect("surface fit failed");
    let p = sfit.position(0.5, 0.5);
    assert!(p.equals_with(Vector3::new(2.0, 2.0, 6.0), 1e-6));

    let in_u = BasisFunctionInput::open_uniform(2, 1);
    let in_v = BasisFunctionInput::open_uniform(2, 1);
    let nurbs_s = NURBSSurface::new(
        &in_u,
        &in_v,
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(0.0, 10.0, 0.0),
            Vector3::new(10.0, 0.0, 0.0),
            Vector3::new(10.0, 10.0, 0.0),
        ],
        vec![1.0, 1.0, 1.0, 1.0],
    );
    assert!(nurbs_s.position(0.5, 0.5).equals_with(Vector3::new(5.0, 5.0, 0.0), 1e-10));
}

#[test]
fn spline_knot_insertion_split_trim_elevate_and_reverse() {
    let orig = Spline::from_control_points(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(3.0, 9.0, 1.0),
            Vector3::new(7.0, 9.0, -1.0),
            Vector3::new(10.0, 0.0, 0.0),
        ],
        3,
        false,
    );

    // Knot insertion preserves exact curve points
    let mut inserted = orig.clone();
    assert!(inserted.insert_knot(0.5, 2));
    for i in 0..=10 {
        let u = i as f64 / 10.0;
        assert!(orig.evaluate(u).unwrap().equals_with(inserted.evaluate(u).unwrap(), 1e-10));
    }

    // Split preserves both halves
    let (left, right) = orig.split(0.4).expect("split failed");
    assert!(left.evaluate(0.2).unwrap().equals_with(orig.evaluate(0.2).unwrap(), 1e-10));
    assert!(right.evaluate(0.7).unwrap().equals_with(orig.evaluate(0.7).unwrap(), 1e-10));

    // Trim preserves sub-interval
    let trimmed = orig.trim(0.2, 0.8).expect("trim failed");
    assert!(trimmed.evaluate(0.5).unwrap().equals_with(orig.evaluate(0.5).unwrap(), 1e-10));

    // Degree elevation preserves exact curve
    let mut elevated = orig.clone();
    assert!(elevated.elevate_degree());
    assert_eq!(elevated.degree, 4);
    for i in 0..=10 {
        let u = i as f64 / 10.0;
        assert!(orig.evaluate(u).unwrap().equals_with(elevated.evaluate(u).unwrap(), 1e-10));
    }

    // Reverse mirrors parameter domain
    let mut rev = orig.clone();
    rev.reverse();
    assert!(rev.evaluate(0.25).unwrap().equals_with(orig.evaluate(0.75).unwrap(), 1e-10));
    assert!(orig.evaluate_derivative(0.5).unwrap().x > 0.0);
}

#[test]
fn acis_sat_codec_and_roundtrip() {
    let lines = vec![
        "700 0 1 0".to_string(),
        "@12 AutoCAD_ACIS A_TEST ^check".to_string(),
        "End-of-ACIS-data".to_string(),
    ];
    for line in &lines {
        let enc = AcisEntityData::encode_sat_line(line).unwrap();
        let dec = AcisEntityData::decode_sat_line(&enc).unwrap();
        assert_eq!(&dec, line);
    }

    let mut solid = Solid3D::from_sat_lines(&lines).unwrap();
    solid.history_handle = Some("0".to_string());
    let body = Body::from_sat_lines(&lines).unwrap();
    let region = Region::from_sat_lines(&lines).unwrap();

    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2010);
    doc.add_entity(solid);
    doc.add_entity(body);
    doc.add_entity(region);

    for is_binary in [false, true] {
        let bytes = doc.save_to_bytes(is_binary).unwrap();
        let loaded = DxfDocument::load_from_bytes(&bytes).unwrap();
        assert_eq!(loaded.entities.solids3d().count(), 1);
        assert_eq!(loaded.entities.bodies().count(), 1);
        assert_eq!(loaded.entities.regions().count(), 1);
        assert_eq!(loaded.entities.solids3d().next().unwrap().acis.sat_lines(), lines.as_slice());
        assert_eq!(loaded.entities.solids3d().next().unwrap().history_handle.as_deref(), Some("0"));
    }

    // Verify real ACIS SAT fixtures from tests/fixtures/acis-sat if present
    let acis_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .unwrap()
        .parent()
        .unwrap()
        .join("tests")
        .join("fixtures")
        .join("acis-sat");
    if acis_dir.exists() {
        let mut decoded_any = false;
        for entry in std::fs::read_dir(&acis_dir).unwrap().flatten() {
            let p = entry.path();
            if p.extension().and_then(|e| e.to_str()) == Some("dxf") {
                if let Ok(d) = DxfDocument::load(&p) {
                    if let Some(s) = d.entities.solids3d().next() {
                        if !s.acis.sat_lines().is_empty() {
                            decoded_any = true;
                        }
                    }
                }
            }
        }
        assert!(decoded_any, "expected at least one ACIS SAT fixture to decode");
    }
}

#[test]
fn table_formula_calculation_stored_table_and_fields() {
    assert_eq!(DxfTableCellAddress::parse_a1("A1").unwrap(), DxfTableCellAddress::new(0, 0));
    assert_eq!(DxfTableCellAddress::parse_a1("AA10").unwrap().to_a1(), "AA10");

    // Precedence & right-associative exponentiation
    let f = DxfTableFormula::parse_scalar("=2^3^2").unwrap();
    assert_eq!(f.evaluate_scalar().unwrap(), 512.0);
    let f2 = DxfTableFormula::parse_scalar("=-2^2").unwrap();
    assert_eq!(f2.evaluate_scalar().unwrap(), -4.0);

    // Overflow-safe AVERAGE
    let f_avg = DxfTableFormula::parse_scalar("=AVERAGE(1e308, 1e308)").unwrap();
    assert!(MathHelper::is_equal_with(f_avg.evaluate_scalar().unwrap(), 1e308, 1e296));

    // StoredTable with formula cells + recalculation + round-trip
    let mut table = StoredTable::new(3, 2);
    table.set_cell_text(0, 0, "Item");
    table.set_cell_text(0, 1, "Value");
    table.set_cell_number(1, 1, 10.0);
    table.set_cell_number(2, 1, 20.0);
    table.set_cell_text(1, 0, "=SUM(B2:B3)");
    table.set_cell_text(2, 0, "=AVERAGE(B2:B3)");
    table.recalculate().unwrap();
    assert_eq!(table.get_cell(1, 0).unwrap().text, "30");
    assert_eq!(table.get_cell(2, 0).unwrap().text, "15");

    // Cycle detection
    let cyc = DxfTableFormula::parse("=A1").unwrap();
    assert!(cyc
        .evaluate(1, 1, |_| TableCellValue::Formula("=A1".into()))
        .is_err());

    // Standard field evaluator
    let mut eval = DxfStandardFieldEvaluator::new();
    eval.set_variable("Author", "netDxf Rust");
    assert_eq!(
        eval.evaluate("%<\\AcExpr (10 + 20 * 3)>%").unwrap(),
        DxfFieldResult::Number(70.0)
    );
    assert_eq!(
        eval.evaluate("%<\\AcVar Author>%").unwrap(),
        DxfFieldResult::Text("netDxf Rust".into())
    );
}

#[test]
fn multileader_geodata_sun_light_ole_section_and_dimension_block_roundtrip() {
    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    doc.build_dimension_blocks = true;

    doc.add_entity(MultiLeader::with_mtext(
        "Callout A",
        Vector3::new(20.0, 20.0, 0.0),
        vec![Vector3::ZERO, Vector3::new(15.0, 15.0, 0.0)],
    ));
    doc.add_entity(Light::new("KeyLight", LightType::Spot, Vector3::new(10.0, 20.0, 30.0)));
    doc.add_entity(Ole2Frame::new(
        Vector3::ZERO,
        Vector3::new(10.0, 10.0, 0.0),
        vec![1, 2, 3, 4, 5],
    ));
    doc.add_entity(OleFrame::new(vec![0xAA, 0xBB]));
    doc.add_entity(Section::new(
        "SecA",
        vec![Vector3::ZERO, Vector3::new(10.0, 0.0, 0.0)],
    ));

    let mut tbl = StoredTable::new(2, 2);
    tbl.set_cell_text(0, 0, "Col1");
    tbl.set_cell_text(0, 1, "Col2");
    tbl.set_cell_number(1, 0, 1.5);
    tbl.set_cell_number(1, 1, 2.5);
    doc.add_entity(tbl);

    doc.add_entity(Dimension::linear(
        Vector2::new(0.0, 0.0),
        Vector2::new(40.0, 0.0),
        10.0,
        0.0,
    ));
    doc.add_entity(Dimension::radial(
        Vector2::new(50.0, 50.0),
        Vector2::new(65.0, 50.0),
    ));

    doc.geo_data = Some(DxfGeoData {
        coordinate_system_definition: "WGS84".to_string(),
        ..Default::default()
    });

    doc.sun = Some(DxfSun {
        intensity: 1.5,
        ..Default::default()
    });

    doc.sun_studies.push(DxfStoredSunStudy {
        name: "Solstice".to_string(),
        julian_dates: vec![(2_460_000, 43_200)],
        ..Default::default()
    });

    for is_binary in [false, true] {
        let bytes = doc.save_to_bytes(is_binary).unwrap();
        let loaded = DxfDocument::load_from_bytes(&bytes).unwrap();

        assert_eq!(loaded.entities.multi_leaders().count(), 1);
        assert_eq!(loaded.entities.multi_leaders().next().unwrap().text, "Callout A");
        assert_eq!(loaded.entities.lights().count(), 1);
        assert_eq!(loaded.entities.lights().next().unwrap().name, "KeyLight");
        assert_eq!(loaded.entities.ole2_frames().count(), 1);
        assert_eq!(loaded.entities.ole2_frames().next().unwrap().binary_data, vec![1, 2, 3, 4, 5]);
        assert_eq!(loaded.entities.ole_frames().count(), 1);
        assert_eq!(loaded.entities.sections().count(), 1);
        assert_eq!(loaded.entities.tables().count(), 1);
        assert_eq!(loaded.geo_data.as_ref().unwrap().coordinate_system_definition, "WGS84");
        assert_eq!(loaded.sun.as_ref().unwrap().intensity, 1.5);
        assert_eq!(loaded.sun_studies.len(), 1);
        assert_eq!(loaded.sun_studies[0].name, "Solstice");

        // Verify DimensionBlock synthesized *D1 and *D2 blocks with geometry
        let d1 = loaded.blocks.get("*D1").expect("synthesized *D1 block missing");
        assert!(d1.entities.len() >= 5, "expected lines, arrows, and MText in *D1");
        let d2 = loaded.blocks.get("*D2").expect("synthesized *D2 block missing");
        assert!(d2.entities.len() >= 4, "expected radius line, center mark, and MText in *D2");
    }
}

#[test]
fn phase20_deep_entity_and_object_behaviors() {
    use netdxf::entities::{
        Attribute, AttributeDefinition, EntityObject, HatchBoundaryPath, HatchBoundaryPathEdge,
        HatchBoundaryPathTypeFlags, Insert, Solid, Trace,
    };
    use netdxf::Matrix3;

    // Dimension::update recomputes measurement and midpoint
    let mut dim = Dimension::linear(Vector2::new(0.0, 0.0), Vector2::new(30.0, 40.0), 5.0, 0.0);
    dim.first_reference_point = Vector3::new(0.0, 0.0, 0.0);
    dim.second_reference_point = Vector3::new(100.0, 0.0, 0.0);
    dim.update();
    assert!((dim.measurement - 100.0).abs() < 1e-6);
    assert!((dim.text_reference_point.x - 50.0).abs() < 1e-6);

    // Attribute definition binding & proxy graphics
    let def = AttributeDefinition::new("TAG1");
    let mut attr = Attribute::new("OLD", "VAL");
    let binding = attr.prepare_definition_binding(Some(def.tag.clone()), false);
    assert_eq!(binding.previous_tag.as_deref(), None);
    assert_eq!(binding.new_tag.as_deref(), Some("TAG1"));
    attr.set_definition(&def);
    assert_eq!(attr.definition(), Some("TAG1"));
    attr.set_proxy_graphics(vec![1, 2, 3]);
    assert_eq!(attr.proxy_graphics(), Some(&[1u8, 2, 3][..]));
    attr.clear_proxy_graphics();
    assert_eq!(attr.proxy_graphics(), None);

    // Insert sequence end record (only created when attributes exist)
    let mut ins = Insert::new("BLOCK_A");
    assert!(ins.end_sequence_record().is_none());
    ins.ensure_attribute_sequence_end();
    assert!(ins.end_sequence_record().is_none());
    ins.attributes.push(Attribute::new("TAG1", "VAL1"));
    ins.ensure_attribute_sequence_end();
    assert!(ins.end_sequence_record().is_some());

    // HatchBoundaryPath::update sets POLYLINE / DERIVED flags based on edges
    let mut path = HatchBoundaryPath::new(vec![HatchBoundaryPathEdge::Line {
        start: Vector2::ZERO,
        end: Vector2::new(10.0, 0.0),
    }]);
    path.update();
    assert!(!path
        .path_type_flags
        .contains(HatchBoundaryPathTypeFlags::POLYLINE));
    assert!(path
        .path_type_flags
        .contains(HatchBoundaryPathTypeFlags::DERIVED));

    // EntityObject::transform_by transforms Solid and Trace vertices
    let mut solid_ent = EntityObject::Solid(Box::new(Solid::new(
        Vector2::new(0.0, 0.0),
        Vector2::new(1.0, 0.0),
        Vector2::new(1.0, 1.0),
        Vector2::new(0.0, 1.0),
    )));
    solid_ent.transform_by(Matrix3::IDENTITY, Vector3::new(10.0, 20.0, 0.0));
    if let EntityObject::Solid(s) = solid_ent {
        assert!((s.first_vertex.x - 10.0).abs() < 1e-6);
        assert!((s.first_vertex.y - 20.0).abs() < 1e-6);
    } else {
        panic!("expected Solid");
    }

    let mut trace_ent = EntityObject::Trace(Box::new(Trace::new(
        Vector2::new(0.0, 0.0),
        Vector2::new(2.0, 0.0),
        Vector2::new(2.0, 2.0),
        Vector2::new(0.0, 2.0),
    )));
    trace_ent.transform_by(Matrix3::IDENTITY, Vector3::new(5.0, -5.0, 0.0));
    if let EntityObject::Trace(t) = trace_ent {
        assert!((t.first_vertex.x - 5.0).abs() < 1e-6);
        assert!((t.first_vertex.y + 5.0).abs() < 1e-6);
    } else {
        panic!("expected Trace");
    }
}
