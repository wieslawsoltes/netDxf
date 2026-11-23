use std::io::{BufReader, Cursor};

use encoding_rs::UTF_8;
use netdxf::io::{BinaryCodeValueReader, ICodeValueReader};

use super::helpers::binary_tag_bytes;

#[test]
fn strict_binary_values_conformance() {
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

    let bytes = binary_tag_bytes(5, b"ABCD", false);
    let mut r =
        BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
    assert!(r.next().is_err());

    let bytes = binary_tag_bytes(1, "  Zażółć λ  \0".as_bytes(), true);
    let mut r =
        BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
    assert!(r.next().unwrap());
    assert_eq!(r.value().as_str(), "  Zażółć λ  ");
}
