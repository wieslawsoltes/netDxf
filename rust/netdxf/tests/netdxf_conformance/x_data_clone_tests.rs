use netdxf::entities::{EntityObject, Line};
use netdxf::tables::ApplicationRegistry;
use netdxf::{DxfDocument, Vector3, XData, XDataCode, XDataRecord, XDataValue};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn xdata_clone_and_wire_isolation() {
    const APP: &str = "DXF_CLONE_CONFORMANCE";

    for len in [0usize, 1, 127] {
        let bytes: Vec<u8> = (0..len).map(|i| i as u8).collect();
        let mut src = XData::new(APP);
        src.x_data_record.push(XDataRecord::new(
            XDataCode::BinaryData,
            XDataValue::Binary(bytes.clone()),
        ));
        let mut copy = src.clone();
        if let XDataValue::Binary(ref mut b) = copy.x_data_record[0].value {
            if !b.is_empty() {
                b[0] = 200;
            }
        }
        if let XDataValue::Binary(ref orig) = src.x_data_record[0].value {
            assert_eq!(orig, &bytes);
        } else {
            panic!("expected BinaryData");
        }
    }

    let mut scalar = XData::new(APP);
    scalar.x_data_record.push(XDataRecord::open_control_string());
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::DatabaseHandle,
        XDataValue::String("ABC123".into()),
    ));
    scalar
        .x_data_record
        .push(XDataRecord::string("Zażółć gęślą jaźń"));
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::LayerName,
        XDataValue::String("Geometry".into()),
    ));
    scalar.x_data_record.push(XDataRecord::int16(-42));
    scalar.x_data_record.push(XDataRecord::int32(-123456));
    scalar.x_data_record.push(XDataRecord::real(12.75));
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::Distance,
        XDataValue::Real(3.5),
    ));
    scalar.x_data_record.push(XDataRecord::new(
        XDataCode::ScaleFactor,
        XDataValue::Real(2.0),
    ));
    scalar
        .x_data_record
        .push(XDataRecord::close_control_string());
    let cloned_scalar = scalar.clone();
    assert_eq!(scalar.x_data_record, cloned_scalar.x_data_record);
    assert_eq!(scalar.x_data_record[0].code, XDataCode::ControlString);

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut src_line = Line::new(Vector3::ZERO, Vector3::UNIT_X);
            let mut xd = XData::new(APP);
            xd.x_data_record.push(XDataRecord::new(
                XDataCode::BinaryData,
                XDataValue::Binary(vec![10, 77, 30]),
            ));
            src_line.common.object.add_xdata(xd);

            let mut copy_line = src_line.clone();
            copy_line.start_point = Vector3::new(5.0, 0.0, 0.0);
            if let XDataValue::Binary(ref mut b) =
                copy_line.common.object.x_data.get_mut(APP).unwrap().x_data_record[0].value
            {
                b[0] = 99;
                b[1] = 20;
            }

            let mut doc = DxfDocument::with_version(version);
            doc.application_registries
                .add(ApplicationRegistry::new(APP).unwrap());
            doc.add_entity(src_line);
            doc.add_entity(copy_line);

            let bytes = doc.save_to_bytes(binary).expect("save xdata clone doc");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load xdata clone doc");
            let orig = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) if l.start_point.x == 0.0 => Some(l),
                    _ => None,
                })
                .unwrap();
            let cloned = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) if l.start_point.x == 5.0 => Some(l),
                    _ => None,
                })
                .unwrap();
            assert_eq!(
                orig.common.object.x_data.get(APP).unwrap().x_data_record[0].value,
                XDataValue::Binary(vec![10, 77, 30])
            );
            assert_eq!(
                cloned.common.object.x_data.get(APP).unwrap().x_data_record[0].value,
                XDataValue::Binary(vec![99, 20, 30])
            );
        }
    }
}
