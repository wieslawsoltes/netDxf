use std::io::{BufReader, Cursor};

use encoding_rs::UTF_8;
use netdxf::io::{ICodeValueReader, TextCodeValueReader};

#[test]
fn portable_double_literal_and_rejection_cases() {
    fn parse_double(token: &str) -> netdxf::Result<f64> {
        let text = format!("10\n{token}\n0\nEOF\n");
        let mut r = TextCodeValueReader::new(BufReader::new(Cursor::new(text.as_bytes())), UTF_8);
        r.next()?;
        Ok(r.value().as_f64())
    }

    let valid_doubles: [(&str, u64); 10] = [
        ("0", 0),
        ("-0", 0x8000_0000_0000_0000),
        ("+0.0", 0),
        (" \t-00.000e+23\t ", 0x8000_0000_0000_0000),
        (".5", 0x3fe0_0000_0000_0000),
        ("1.", 0x3ff0_0000_0000_0000),
        ("4.9406564584124654e-324", 1),
        ("-1e-99999999999999999999", 0x8000_0000_0000_0000),
        ("1.7976931348623157e308", 0x7fef_ffff_ffff_ffff),
        ("9007199254740993", 0x4340_0000_0000_0000),
    ];
    for (token, expected_bits) in valid_doubles {
        let v = parse_double(token).unwrap_or_else(|e| panic!("token={token:?}: {e}"));
        assert_eq!(v.to_bits(), expected_bits, "token={token:?}");
    }

    for bad in [
        "",
        " ",
        "garbage",
        "1,25",
        "1.2.3",
        "1e",
        "--1",
        "1\0",
        "NaN",
        "nan",
        "+NaN",
        "Infinity",
        "-Infinity",
        "1e9999",
        "-1e9999",
        "\u{00a0}1",
        "1\u{2003}",
        "\u{0661}",
    ] {
        assert!(parse_double(bad).is_err(), "bad double: {bad:?}");
    }
}
