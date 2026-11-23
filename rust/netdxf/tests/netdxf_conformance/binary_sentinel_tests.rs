use std::io::{BufReader, Cursor};

use encoding_rs::UTF_8;
use netdxf::io::{BinaryCodeValueReader, ICodeValueReader, BINARY_DXF_SENTINEL};

use super::helpers::OneByteReader;

#[test]
fn binary_sentinel_conformance() {
    for len in 0..BINARY_DXF_SENTINEL.len() {
        let cur = BufReader::new(Cursor::new(&BINARY_DXF_SENTINEL[..len]));
        assert!(BinaryCodeValueReader::new(cur, UTF_8, false).is_err());
    }
    for idx in 0..BINARY_DXF_SENTINEL.len() {
        let mut corrupt = *BINARY_DXF_SENTINEL;
        corrupt[idx] ^= 0x01;
        let cur = BufReader::new(Cursor::new(corrupt));
        assert!(BinaryCodeValueReader::new(cur, UTF_8, false).is_err());
    }
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
