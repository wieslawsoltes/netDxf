use netdxf::entities::{EntityObject, Line, MText};
use netdxf::header::DxfVersion;
use netdxf::tables::ApplicationRegistry;
use netdxf::{DxfDocument, Vector3, XData, XDataRecord, XDataValue};

#[test]
fn mtext_unicode_chunk_budget_and_roundtrip() {
    let mut samples: Vec<String> = Vec::new();
    for &count in &[0usize, 1, 248, 249, 250, 251, 499, 500, 501] {
        samples.push("a".repeat(count));
    }
    for &prefix in &[0usize, 1, 246, 247, 248, 249, 250] {
        samples.push(format!(
            "{}{}{}",
            "x".repeat(prefix),
            "\u{1F680}",
            "y".repeat(503)
        ));
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
