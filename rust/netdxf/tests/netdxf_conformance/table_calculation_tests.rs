use netdxf::tables::{DxfTableCalculation, DxfTableCellAddress, DxfTableFormula, TableCellValue};

#[test]
fn table_calculation_conformance() {
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

    for bad in ["=1/0", "=0^-1", "=(-1)^0.5", "=1e308*2"] {
        let f = DxfTableFormula::parse(bad).unwrap();
        assert!(
            f.evaluate(1, 1, |_| TableCellValue::Float(0.0)).is_err(),
            "should reject arithmetic error: {bad}"
        );
    }

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

    assert!(DxfTableFormula::parse("=B1")
        .unwrap()
        .evaluate(1, 1, |_| TableCellValue::Int(0))
        .is_err());
    assert!(DxfTableFormula::parse(&format!("={}1", "-".repeat(300))).is_err());
    assert!(
        DxfTableFormula::parse(&format!("={}1{}", "(".repeat(300), ")".repeat(300))).is_err()
    );

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

    for n in 1..=100 {
        let f = DxfTableFormula::parse_scalar(&format!("={n}*({n}+1)/2")).unwrap();
        assert_eq!(f.evaluate_scalar().unwrap(), (n * (n + 1) / 2) as f64);
    }
}
