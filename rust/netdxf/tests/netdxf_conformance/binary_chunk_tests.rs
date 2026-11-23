use std::io::{BufReader, Cursor};

use encoding_rs::UTF_8;
use netdxf::io::{BinaryCodeValueReader, CodeValue, ICodeValueReader};

use super::helpers::binary_tag_bytes;

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

        let mut trunc_payload = vec![10u8];
        trunc_payload.extend_from_slice(&[1, 2, 3]);
        let bytes = binary_tag_bytes(code, &trunc_payload, false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err());

        let bytes = binary_tag_bytes(code, &[], false);
        let mut r =
            BinaryCodeValueReader::new(BufReader::new(Cursor::new(bytes)), UTF_8, false).unwrap();
        assert!(r.next().is_err());
    }
}
