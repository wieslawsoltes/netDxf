use std::io::{BufReader, Cursor};

use encoding_rs::UTF_8;
use netdxf::io::{CodeValue, ICodeValueReader, TextCodeValueReader};

fn read_text(code: i16, val: &str) -> netdxf::Result<CodeValue> {
    let text = format!("{code}\n{val}\n0\nEOF\n");
    let mut r = TextCodeValueReader::new(BufReader::new(Cursor::new(text.as_bytes())), UTF_8);
    r.next()?;
    Ok(r.value().clone())
}

#[test]
fn strict_text_values_and_group_code_nul_conformance() {
    assert_eq!(read_text(70, "-32768").unwrap(), CodeValue::Int16(i16::MIN));
    assert_eq!(read_text(70, "32767").unwrap(), CodeValue::Int16(i16::MAX));
    assert_eq!(read_text(70, " \t+0012 \t").unwrap(), CodeValue::Int16(12));
    for bad in ["32768", "-32769", "", " ", "garbage", "1.0", "1,000", "--1", "1\0"] {
        assert!(read_text(70, bad).is_err(), "bad i16: {bad:?}");
    }

    assert_eq!(read_text(90, "-2147483648").unwrap(), CodeValue::Int32(i32::MIN));
    assert_eq!(read_text(90, "2147483647").unwrap(), CodeValue::Int32(i32::MAX));
    assert!(read_text(90, "2147483648").is_err());
    assert_eq!(
        read_text(160, "-9223372036854775808").unwrap(),
        CodeValue::Int64(i64::MIN)
    );
    assert_eq!(
        read_text(160, "9223372036854775807").unwrap(),
        CodeValue::Int64(i64::MAX)
    );
    assert!(read_text(160, "9223372036854775808").is_err());

    assert_eq!(read_text(290, " 0 ").unwrap(), CodeValue::Bool(false));
    assert_eq!(read_text(290, " +01 ").unwrap(), CodeValue::Bool(true));
    for bad in ["", "true", "false", "-1", "2", "255", "1.0", "1\0", "garbage"] {
        assert!(read_text(290, bad).is_err(), "bad bool: {bad:?}");
    }

    assert_eq!(read_text(5, "0").unwrap().as_str(), "0");
    assert_eq!(read_text(5, " \t000aF \t").unwrap().as_str(), "AF");
    assert_eq!(
        read_text(5, "ffffffffffffffff").unwrap().as_str(),
        "FFFFFFFFFFFFFFFF"
    );
    for bad in ["", " ", "G", "-1", "+1", "0xFF", "1 2", "1\0", "10000000000000000"] {
        assert!(read_text(5, bad).is_err(), "bad handle: {bad:?}");
    }

    assert_eq!(
        read_text(1, "  literal text  ").unwrap().as_str(),
        "  literal text  "
    );

    for suffix in ["\0", "\0\0", " \0", "\t\0"] {
        let text = format!("10{suffix}\n7.5\n");
        let mut r = TextCodeValueReader::new(BufReader::new(Cursor::new(text.as_bytes())), UTF_8);
        assert!(r.next().is_err());
    }
}
