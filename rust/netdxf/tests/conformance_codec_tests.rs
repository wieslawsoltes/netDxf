use std::io::{BufReader, Cursor, Read};

use encoding_rs::UTF_8;
use netdxf::entities::{EntityObject, Line};
use netdxf::header::DxfVersion;
use netdxf::io::{
    BinaryCodeValueReader, CodeValue, ICodeValueReader, TextCodeValueReader, BINARY_DXF_SENTINEL,
};
use netdxf::tables::ApplicationRegistry;
use netdxf::{DxfDocument, Vector3, XData, XDataCode, XDataRecord, XDataValue};

const SUPPORTED_VERSIONS: [DxfVersion; 6] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
    DxfVersion::AutoCad2013,
    DxfVersion::AutoCad2018,
];

struct OneByteReader<'a> {
    data: &'a [u8],
    pos: usize,
}

impl<'a> Read for OneByteReader<'a> {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        if self.pos >= self.data.len() || buf.is_empty() {
            return Ok(0);
        }
        buf[0] = self.data[self.pos];
        self.pos += 1;
        Ok(1)
    }
}

fn binary_tag_bytes(code: i16, payload: &[u8], with_eof: bool) -> Vec<u8> {
    let mut out = Vec::new();
    out.extend_from_slice(BINARY_DXF_SENTINEL);
    out.extend_from_slice(&code.to_le_bytes());
    out.extend_from_slice(payload);
    if with_eof {
        out.extend_from_slice(&0i16.to_le_bytes());
        out.extend_from_slice(b"EOF\0");
    }
    out
}

#[test]
fn binary_sentinel_conformance() {
    // Truncated 0..22 bytes
    for len in 0..BINARY_DXF_SENTINEL.len() {
        let cur = BufReader::new(Cursor::new(&BINARY_DXF_SENTINEL[..len]));
        assert!(BinaryCodeValueReader::new(cur, UTF_8, false).is_err());
    }
    // Single-bit corrupt at each offset
    for idx in 0..BINARY_DXF_SENTINEL.len() {
        let mut corrupt = *BINARY_DXF_SENTINEL;
        corrupt[idx] ^= 0x01;
        let cur = BufReader::new(Cursor::new(corrupt));
        assert!(BinaryCodeValueReader::new(cur, UTF_8, false).is_err());
    }
    // Fragmented 1-byte-at-a-time stream
    let frag = BufReader::with_capacity(
        1,
        OneByteReader {
            data: BINARY_DXF_SENTINEL,
            pos: 0,
        },
    );
    let r = BinaryCodeValueReader::new(frag, UTF_8, false).expect("fragmented sentinel");
    assert_eq!(r.current_position(), 22);
}

#[test]
fn binary_chunk_conformance() {
    let mut codes: Vec<i16> = (310..=319).collect();
    codes.push(1004);

    for code in codes {
        for len in [0usize, 1, 2, 127, 128, 254, 255] {
            let mut payload = vec![len as u8];
            payload.extend((0..len).map(|i| i as u8));
            let bytes = binary_tag_bytes(code, &payload, true);
            let mut r = BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false)
                .unwrap();
            assert!(r.next().unwrap());
            assert_eq!(r.code(), code);
            let expected: Vec<u8> = (0..len).map(|i| i as u8).collect();
            assert_eq!(r.value(), &CodeValue::Bytes(expected));
            assert!(r.next().unwrap());
            assert_eq!(r.value().as_str(), "EOF");
        }

        // Truncated chunk payload
        let mut trunc_payload = vec![10u8];
        trunc_payload.extend_from_slice(&[1, 2, 3]);
        let bytes = binary_tag_bytes(code, &trunc_payload, false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err());

        // Missing count byte
        let bytes = binary_tag_bytes(code, &[], false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err());
    }
}

#[test]
fn strict_binary_values_conformance() {
    // Finite IEEE bits preserved
    for val in [
        0.0_f64,
        -0.0,
        -1.25e12,
        f64::from_bits(1),
        -f64::from_bits(1),
        f64::MAX,
        f64::MIN,
    ] {
        let bytes = binary_tag_bytes(10, &val.to_le_bytes(), true);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().unwrap());
        assert_eq!(r.value().as_f64().to_bits(), val.to_bits());
    }

    // Non-finite doubles rejected
    for bits in [
        0x7ff0_0000_0000_0000_u64,
        0xfff0_0000_0000_0000_u64,
        0x7ff0_0000_0000_0001_u64,
        0x7ff8_0000_0000_0001_u64,
        0xfff8_0000_0000_1234_u64,
    ] {
        let bytes = binary_tag_bytes(10, &bits.to_le_bytes(), false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err());
    }

    // Boolean 0/1 valid, 2..=255 rejected
    for (b, expected) in [(0u8, false), (1u8, true)] {
        let bytes = binary_tag_bytes(290, &[b], true);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().unwrap());
        assert_eq!(r.value().as_bool(), expected);
    }
    for b in 2u8..=255 {
        let bytes = binary_tag_bytes(290, &[b], false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err(), "byte {b} must be rejected for boolean");
    }

    // Handle validation & normalization
    for (raw, expected) in [
        ("0\0", "0"),
        (" \t000af \t\0", "AF"),
        ("ffffffffffffffff\0", "FFFFFFFFFFFFFFFF"),
    ] {
        let bytes = binary_tag_bytes(5, raw.as_bytes(), true);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().unwrap());
        assert_eq!(r.value().as_str(), expected);
    }
    for bad in ["\0", " \0", "G\0", "-1\0", "0xFF\0", "10000000000000000\0"] {
        let bytes = binary_tag_bytes(5, bad.as_bytes(), false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err(), "bad binary handle {bad:?} must reject");
    }

    // Unterminated string rejected
    let bytes = binary_tag_bytes(5, b"ABCD", false);
    let mut r =
        BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
    assert!(r.next().is_err());

    // Ordinary UTF-8 string whitespace preserved
    let bytes = binary_tag_bytes(1, "  Zażółć λ  \0".as_bytes(), true);
    let mut r =
        BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
    assert!(r.next().unwrap());
    assert_eq!(r.value().as_str(), "  Zażółć λ  ");
}

#[test]
fn strict_text_values_nul_and_hex_conformance() {
    fn read_text(code: i16, val: &str) -> netdxf::Result<CodeValue> {
        let text = format!("{code}\n{val}\n0\nEOF\n");
        let mut r = TextCodeValueReader::new(BufReader::new(Cursor::new(text.as_bytes())), UTF_8);
        r.next()?;
        Ok(r.value().clone())
    }

    // i16 endpoints & overflow
    assert_eq!(read_text(70, "-32768").unwrap(), CodeValue::Int16(i16::MIN));
    assert_eq!(read_text(70, "32767").unwrap(), CodeValue::Int16(i16::MAX));
    assert_eq!(read_text(70, " \t+0012 \t").unwrap(), CodeValue::Int16(12));
    for bad in ["32768", "-32769", "", " ", "garbage", "1.0", "1,000", "--1", "1\0"] {
        assert!(read_text(70, bad).is_err(), "bad i16: {bad:?}");
    }

    // i32 & i64 endpoints & overflow
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

    // Doubles including portable double edge cases
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
        let v = read_text(10, token).unwrap().as_f64();
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
        assert!(read_text(10, bad).is_err(), "bad double: {bad:?}");
    }

    // Booleans
    assert_eq!(read_text(290, " 0 ").unwrap(), CodeValue::Bool(false));
    assert_eq!(read_text(290, " +01 ").unwrap(), CodeValue::Bool(true));
    for bad in ["", "true", "false", "-1", "2", "255", "1.0", "1\0", "garbage"] {
        assert!(read_text(290, bad).is_err(), "bad bool: {bad:?}");
    }

    // Handles
    assert_eq!(read_text(5, "0").unwrap().as_str(), "0");
    assert_eq!(read_text(5, " \t000aF \t").unwrap().as_str(), "AF");
    assert_eq!(
        read_text(5, "ffffffffffffffff").unwrap().as_str(),
        "FFFFFFFFFFFFFFFF"
    );
    for bad in ["", " ", "G", "-1", "+1", "0xFF", "1 2", "1\0", "10000000000000000"] {
        assert!(read_text(5, bad).is_err(), "bad handle: {bad:?}");
    }

    // Ordinary strings preserve leading/trailing whitespace
    assert_eq!(
        read_text(1, "  literal text  ").unwrap().as_str(),
        "  literal text  "
    );

    // Group code NUL rejection
    for suffix in ["\0", "\0\0", " \0", "\t\0"] {
        let text = format!("10{suffix}\n7.5\n");
        let mut r = TextCodeValueReader::new(BufReader::new(Cursor::new(text.as_bytes())), UTF_8);
        assert!(r.next().is_err());
    }

    // Hex binary chunks (codes 310..=319 & 1004)
    assert_eq!(
        read_text(310, "00aF10Ff").unwrap(),
        CodeValue::Bytes(vec![0, 175, 16, 255])
    );
    assert_eq!(read_text(1004, "").unwrap(), CodeValue::Bytes(vec![]));
    for bad in ["0", "ABC", "0G", "G0", " 0", "0 ", "-1", "0x", "00\0F"] {
        assert!(read_text(310, bad).is_err(), "bad hex chunk: {bad:?}");
    }
}

#[test]
fn xdata_binary_chunks_all_256_bytes_roundtrip() {
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
