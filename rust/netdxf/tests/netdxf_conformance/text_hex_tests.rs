use std::io::{BufReader, Cursor};

use encoding_rs::UTF_8;
use netdxf::entities::{EntityObject, Line};
use netdxf::io::{CodeValue, ICodeValueReader, TextCodeValueReader};
use netdxf::tables::ApplicationRegistry;
use netdxf::{DxfDocument, Vector3, XData, XDataCode, XDataRecord, XDataValue};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn text_hex_binary_chunks_and_xdata_roundtrip() {
    fn read_hex(code: i16, val: &str) -> netdxf::Result<CodeValue> {
        let text = format!("{code}\n{val}\n0\nEOF\n");
        let mut r = TextCodeValueReader::new(BufReader::new(Cursor::new(text.as_bytes())), UTF_8);
        r.next()?;
        Ok(r.value().clone())
    }

    assert_eq!(
        read_hex(310, "00aF10Ff").unwrap(),
        CodeValue::Bytes(vec![0, 175, 16, 255])
    );
    assert_eq!(read_hex(1004, "").unwrap(), CodeValue::Bytes(vec![]));
    for bad in ["0", "ABC", "0G", "G0", " 0", "0 ", "-1", "0x", "00\0F"] {
        assert!(read_hex(310, bad).is_err(), "bad hex chunk: {bad:?}");
    }

    const APP: &str = "DXF_HEX_CONFORMANCE";
    let expected: Vec<u8> = (0u8..=255).collect();

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            doc.application_registries
                .add(ApplicationRegistry::new(APP).unwrap());
            let mut line = Line::new(Vector3::ZERO, Vector3::new(1.0, 2.0, 3.0));
            let mut xd = XData::new(APP);
            for chunk in expected.chunks(127) {
                xd.x_data_record.push(XDataRecord::new(
                    XDataCode::BinaryData,
                    XDataValue::Binary(chunk.to_vec()),
                ));
            }
            line.common.object.add_xdata(xd);
            doc.add_entity(line);

            let bytes = doc.save_to_bytes(binary).expect("save hex xdata");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load hex xdata");
            let loaded_line = loaded
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) => Some(l),
                    _ => None,
                })
                .unwrap();
            let actual_xd = loaded_line.common.object.x_data.get(APP).unwrap();
            assert_eq!(actual_xd.x_data_record.len(), 3);
            let combined: Vec<u8> = actual_xd
                .x_data_record
                .iter()
                .flat_map(|r| match &r.value {
                    XDataValue::Binary(b) => b.as_slice(),
                    _ => panic!("expected BinaryData"),
                })
                .copied()
                .collect();
            assert_eq!(combined, expected);
        }
    }
}
