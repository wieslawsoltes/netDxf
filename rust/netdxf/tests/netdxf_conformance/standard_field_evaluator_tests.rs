use netdxf::entities::{EntityObject, StoredTable};
use netdxf::header::DxfVersion;
use netdxf::objects::{DxfFieldResult, DxfStandardFieldEvaluator, DxfStoredField};
use netdxf::DxfDocument;

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
