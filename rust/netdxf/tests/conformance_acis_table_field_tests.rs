use netdxf::entities::{AcisEntityData, Body, EntityObject, Region, Solid3D, StoredTable};
use netdxf::header::DxfVersion;
use netdxf::objects::{DxfFieldResult, DxfStandardFieldEvaluator, DxfStoredField};
use netdxf::tables::{DxfTableCalculation, DxfTableCellAddress, DxfTableFormula, TableCellValue};
use netdxf::DxfDocument;

const SAT_VERSIONS: [DxfVersion; 4] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
];

#[test]
fn acis_sat_codec_printable_chunking_and_validation() {
    let printable: String = (32u8..=126).map(|c| c as char).collect();
    let long_line = format!("{}{}{}", "0".repeat(254), "A", "C".repeat(260));
    let lines = vec![
        printable,
        "  leading and trailing  ".to_string(),
        String::new(),
        long_line,
        "700 0 1 0".to_string(),
    ];

    let mut data = AcisEntityData::new();
    data.set_sat_lines(&lines).expect("encode valid SAT lines");
    assert!(
        data.encoded_sat_chunks().iter().any(|c| c.group_code == 3),
        "long SAT line should be chunked with group 3"
    );
    assert!(
        data.encoded_sat_chunks().iter().all(|c| c.text.len() <= 255),
        "SAT chunk must not exceed 255 chars"
    );
    assert_eq!(data.sat_lines(), lines.as_slice());

    // Reject non-printable / non-ASCII SAT lines atomically (without mutating existing payload)
    assert!(data.set_sat_lines(["valid", "invalid\nline"]).is_err());
    assert!(data.set_sat_lines(["valid", "caf\u{00e9}"]).is_err());
    assert!(data.set_sat_lines(["\0"]).is_err());
    assert!(data.set_sat_lines(["\x7f"]).is_err());
    assert_eq!(data.sat_lines(), lines.as_slice());
}

#[test]
fn acis_sat_entities_wire_roundtrip_all_sat_versions() {
    let sat_lines = vec![
        "700 0 1 0".to_string(),
        "@7 netDxf @7 RustSAT @24 Mon Nov 23 12:00:00 2026".to_string(),
        "body $-1 -1 $-1 $1 $-1 $-1 #".to_string(),
        "End-of-ACIS-data".to_string(),
    ];

    for &version in &SAT_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);

            let mut solid = Solid3D::new();
            solid.acis.set_sat_lines(&sat_lines).unwrap();
            let mut body = Body::new();
            body.acis.set_sat_lines(&sat_lines).unwrap();
            let mut region = Region::new();
            region.acis.set_sat_lines(&sat_lines).unwrap();

            doc.add_entity(solid);
            doc.add_entity(body);
            doc.add_entity(region);

            let bytes = doc.save_to_bytes(binary).expect("save ACIS entities");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load ACIS entities");

            let loaded_solid = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Solid3D(s) => Some(s),
                    _ => None,
                })
                .expect("Solid3D");
            let loaded_body = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Body(b) => Some(b),
                    _ => None,
                })
                .expect("Body");
            let loaded_region = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Region(r) => Some(r),
                    _ => None,
                })
                .expect("Region");

            assert_eq!(loaded_solid.acis.sat_lines(), sat_lines.as_slice());
            assert_eq!(loaded_body.acis.sat_lines(), sat_lines.as_slice());
            assert_eq!(loaded_region.acis.sat_lines(), sat_lines.as_slice());
        }
    }
}

#[test]
fn table_formula_conformance_and_average_safety() {
    let cell_resolver = |a: DxfTableCellAddress| {
        if a == DxfTableCellAddress::parse_a1("A1").unwrap() {
            TableCellValue::Int(2)
        } else if a == DxfTableCellAddress::parse_a1("B2").unwrap() {
            TableCellValue::Float(3.0)
        } else if a == DxfTableCellAddress::parse_a1("B1").unwrap() {
            TableCellValue::Text("text".into())
        } else {
            TableCellValue::Empty
        }
    };

    let cases: [(&str, f64); 18] = [
        ("=1+2*3", 7.0),
        ("=(1+2)*3", 9.0),
        ("=2^3^2", 512.0),
        ("=-2^2", -4.0),
        ("=(-2)^2", 4.0),
        ("=2^-3", 0.125),
        ("=1e2+.5", 100.5),
        ("=SUM(1,2,3)", 6.0),
        ("=average(2,4)", 3.0),
        ("=MIN(5,-2,6)", -2.0),
        ("=MAX(5,-2,6)", 6.0),
        ("=SUM(1e16,1,-1e16)", 1.0),
        ("=SUM(A1:B2)", 5.0),
        ("=AVERAGE(B2:A1)", 2.5),
        ("=COUNT(A1:B2)", 2.0),
        ("=COUNT(A1,B1)", 1.0),
        ("=$A$1+$B$2", 5.0),
        ("=SUM(A1:B2,MAX(7,8))", 13.0),
    ];
    for (expr, expected) in cases {
        let f = DxfTableFormula::parse(expr).unwrap_or_else(|e| panic!("parse {expr}: {e}"));
        let actual = f
            .evaluate(2, 2, cell_resolver)
            .unwrap_or_else(|e| panic!("eval {expr}: {e}"));
        assert_eq!(actual, expected, "formula {expr}");
    }

    // Address roundtrip & rejection
    for valid in ["A1", "Z1", "AA1", "ZZ999", "AAA1000", "$a$1"] {
        let addr = DxfTableCellAddress::parse_a1(valid).unwrap();
        assert_eq!(
            addr,
            DxfTableCellAddress::parse_a1(&addr.to_string()).unwrap()
        );
    }
    for invalid in ["", "A0", "0A", "A-1", "A1x", "$", "A$"] {
        assert!(
            DxfTableCellAddress::parse_a1(invalid).is_err(),
            "should reject address: {invalid:?}"
        );
    }

    // Parse rejection
    for bad in [
        "",
        "1+2",
        "=",
        "=1+",
        "=()",
        "=SUM()",
        "=A1:B2",
        "=1,2",
        "=2(3)",
        "=SUM(1:2)",
        "=A0",
        "=1e",
        "=(1",
        "=1)",
        "=EXEC(1)",
    ] {
        assert!(
            DxfTableFormula::parse(bad).is_err(),
            "should reject formula: {bad:?}"
        );
    }

    // Arithmetic rejection (division by zero, overflow, NaN)
    for bad in ["=1/0", "=0^-1", "=(-1)^0.5", "=1e308*2"] {
        let f = DxfTableFormula::parse(bad).unwrap();
        assert!(
            f.evaluate(1, 1, |_| TableCellValue::Float(0.0)).is_err(),
            "should reject arithmetic error: {bad}"
        );
    }

    // Empty aggregates
    for func in ["AVERAGE", "MIN", "MAX"] {
        let f = DxfTableFormula::parse(&format!("={func}(A1:A2)")).unwrap();
        assert!(f.evaluate(2, 1, |_| TableCellValue::Empty).is_err());
    }
    assert_eq!(
        DxfTableFormula::parse("=SUM(A1:A2)")
            .unwrap()
            .evaluate(2, 1, |_| TableCellValue::Empty)
            .unwrap(),
        0.0
    );

    // Overflow-safe AVERAGE(f64::MAX, f64::MAX)
    let avg_max = DxfTableFormula::parse("=AVERAGE(A1:A2)")
        .unwrap()
        .evaluate(2, 1, |_| TableCellValue::Float(f64::MAX))
        .unwrap();
    assert_eq!(avg_max, f64::MAX);

    // Memoization: resolver called once for =A1+A1+A1
    let mut calls = 0usize;
    let res = DxfTableFormula::parse("=A1+A1+A1")
        .unwrap()
        .evaluate(1, 1, |_| {
            calls += 1;
            TableCellValue::Int(3)
        })
        .unwrap();
    assert_eq!(res, 9.0);
    assert_eq!(calls, 1);

    // Bounds & deep nesting rejection
    assert!(DxfTableFormula::parse("=B1")
        .unwrap()
        .evaluate(1, 1, |_| TableCellValue::Int(0))
        .is_err());
    assert!(DxfTableFormula::parse(&format!("={}1", "-".repeat(300))).is_err());
    assert!(
        DxfTableFormula::parse(&format!("={}1{}", "(".repeat(300), ")".repeat(300))).is_err()
    );

    // Topological dependencies & cycle detection via DxfTableCalculation::evaluate_grid
    let results = DxfTableCalculation::evaluate_grid(1, 3, |addr| match (addr.row, addr.column) {
        (0, 0) => TableCellValue::Formula("=3".into()),
        (0, 1) => TableCellValue::Formula("=A1*2".into()),
        (0, 2) => TableCellValue::Formula("=B1+A1".into()),
        _ => TableCellValue::Empty,
    })
    .unwrap();
    assert_eq!(results[&DxfTableCellAddress::parse_a1("C1").unwrap()], 9.0);

    assert!(DxfTableCalculation::evaluate_grid(1, 2, |addr| match (addr.row, addr.column) {
        (0, 0) => TableCellValue::Formula("=B1+1".into()),
        (0, 1) => TableCellValue::Formula("=A1+1".into()),
        _ => TableCellValue::Empty,
    })
    .is_err());

    // Triangular numbers 1..=100
    for n in 1..=100 {
        let f = DxfTableFormula::parse_scalar(&format!("={n}*({n}+1)/2")).unwrap();
        assert_eq!(f.evaluate_scalar().unwrap(), (n * (n + 1) / 2) as f64);
    }
}

#[test]
fn standard_field_evaluator_and_stored_table_conformance() {
    let mut eval = DxfStandardFieldEvaluator::new();
    eval.set_variable("Amount", "21");
    eval.set_variable("Title", "Beam");

    assert_eq!(
        eval.evaluate(r"%<\AcVar Amount>%").unwrap(),
        DxfFieldResult::Text("21".into())
    );
    assert_eq!(
        eval.evaluate(r"%<\AcExpr (2^3 + 6/4)>%").unwrap(),
        DxfFieldResult::Number(9.5)
    );
    assert_eq!(
        eval.evaluate(r"%<\AcVar Title>%").unwrap(),
        DxfFieldResult::Text("Beam".into())
    );
    assert!(eval.evaluate(r"%<\PrivateEvaluator inert>%").is_err());

    let field = DxfStoredField::new("AcExpr", r"%<\AcExpr (2^3 + 6/4)>%");
    assert_eq!(field.evaluator_id, "AcExpr");

    // StoredTable roundtrip
    let mut table = StoredTable::new(3, 2);
    table.set_cell_text(0, 0, "Header");
    table.set_cell_text(1, 0, "10");
    table.set_cell_text(2, 0, "20");

    let mut doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    doc.add_entity(table);
    for &binary in &[false, true] {
        let bytes = doc.save_to_bytes(binary).expect("save StoredTable");
        let loaded = DxfDocument::load_from_bytes(&bytes).expect("load StoredTable");
        let t = loaded
            .entities
            .iter()
            .find_map(|e| match e {
                EntityObject::Table(t) => Some(t.as_ref()),
                _ => None,
            })
            .expect("StoredTable");
        assert_eq!(t.row_count, 3);
        assert_eq!(t.column_count, 2);
        assert_eq!(t.get_cell(0, 0).map(|c| c.text.as_str()), Some("Header"));
    }
}
