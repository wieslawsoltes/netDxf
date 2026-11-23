use netdxf::tables::{DxfTableFormula, TableCellValue};

#[test]
fn table_average_overflow_safety_and_policies() {
    let avg_max = DxfTableFormula::parse("=AVERAGE(A1:A2)")
        .unwrap()
        .evaluate(2, 1, |_| TableCellValue::Float(f64::MAX))
        .unwrap();
    assert_eq!(avg_max, f64::MAX);

    let avg_mixed = DxfTableFormula::parse("=AVERAGE(A1:A4)")
        .unwrap()
        .evaluate(4, 1, |a| match a.row {
            0 | 3 => TableCellValue::Float(f64::MAX),
            1 => TableCellValue::Empty,
            _ => TableCellValue::Text("ignored".into()),
        })
        .unwrap();
    assert_eq!(avg_mixed, f64::MAX);

    let avg_i32 = DxfTableFormula::parse("=AVERAGE(A1:A2)")
        .unwrap()
        .evaluate(2, 1, |a| {
            if a.row == 0 {
                TableCellValue::Int(i32::MIN)
            } else {
                TableCellValue::Int(i32::MAX)
            }
        })
        .unwrap();
    assert_eq!(avg_i32, -0.5);

    assert!(DxfTableFormula::parse_scalar("=SUM(1e308,1e308)")
        .unwrap()
        .evaluate_scalar()
        .is_err());
    assert!(DxfTableFormula::parse_scalar("=AVERAGE(1e308*2,1)")
        .unwrap()
        .evaluate_scalar()
        .is_err());
}
