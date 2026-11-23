use std::io::{Cursor, Seek, SeekFrom};

use netdxf::entities::Line;
use netdxf::header::DxfVersion;
use netdxf::io::{DxfReader, BINARY_DXF_SENTINEL};
use netdxf::{DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

#[test]
fn header_probe_conformance() {
    for len in 0..BINARY_DXF_SENTINEL.len() {
        let (ver, is_bin) =
            DxfReader::check_version(Cursor::new(&BINARY_DXF_SENTINEL[..len])).unwrap();
        assert_eq!(ver, DxfVersion::Unknown);
        assert!(!is_bin);
    }

    let mut base_doc = DxfDocument::with_version(DxfVersion::AutoCad2018);
    base_doc.add_entity(Line::new(Vector3::ZERO, Vector3::UNIT_X));
    let bin_bytes = base_doc.save_to_bytes(true).unwrap();
    for offset in 0..BINARY_DXF_SENTINEL.len() {
        let mut corrupt = bin_bytes.clone();
        corrupt[offset] ^= 1;
        let (ver, is_bin) = DxfReader::check_version(Cursor::new(&corrupt)).unwrap();
        assert_eq!(ver, DxfVersion::Unknown);
        assert!(!is_bin);
    }

    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            doc.add_entity(Line::new(Vector3::ZERO, Vector3::UNIT_X));
            let bytes = doc.save_to_bytes(binary).unwrap();
            for offset in [0usize, 7, 31] {
                let mut prefixed = vec![0xCCu8; offset];
                prefixed.extend_from_slice(&bytes);
                let mut cursor = Cursor::new(prefixed);
                cursor.seek(SeekFrom::Start(offset as u64)).unwrap();
                let (probed, found_bin) = DxfReader::check_version(&mut cursor).unwrap();
                assert_eq!(probed, version);
                assert_eq!(found_bin, binary);
                assert_eq!(cursor.position(), offset as u64);
                let loaded = DxfReader::read(&mut cursor).unwrap();
                assert_eq!(loaded.drawing_variables.acad_ver, version);
            }
        }
    }

    for text in [
        "0\nEOF",
        "0\nEOF\n",
        "999\nHEADER\n0\nEOF\n",
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n",
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n70\n1\n0\nENDSEC\n0\nEOF\n",
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n3\nAC1032\n0\nENDSEC\n0\nEOF\n",
        "0\nSECTION\n1\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n",
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n9\nAC1032\n0\nENDSEC\n0\nEOF\n",
        "0\nSECTION\n2\nCLASSES\n0\nEOF\n",
        "0\nSECTION\n2\nENTITIES\n0\nTEXT\n1\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n",
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC9999\n0\nENDSEC\n0\nEOF\n",
    ] {
        let (ver, is_bin) = DxfReader::check_version(Cursor::new(text.as_bytes())).unwrap();
        assert_eq!(ver, DxfVersion::Unknown, "should be Unknown for: {text:?}");
        assert!(!is_bin);
    }

    for word in ["EOF", "HEADER", "ENDSEC", "SECTION", "$ACADVER"] {
        let text = format!(
            "999\n{word}\n0\nSECTION\n999\n{word}\n2\nHEADER\n999\n{word}\n9\n$ACADVER\n999\n{word}\n1\nAC1032\n0\nENDSEC\n0\nEOF\n"
        );
        let (ver, is_bin) = DxfReader::check_version(Cursor::new(text.as_bytes())).unwrap();
        assert_eq!(ver, DxfVersion::AutoCad2018);
        assert!(!is_bin);
    }

    let after_classes = "0\nSECTION\n2\nCLASSES\n0\nCLASS\n1\nHEADER\n90\n0\n280\n0\n281\n0\n0\nENDSEC\n0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n";
    let (ver, _) = DxfReader::check_version(Cursor::new(after_classes.as_bytes())).unwrap();
    assert_eq!(ver, DxfVersion::AutoCad2018);

    let partial =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\ngarbage-after-selected-variable\n";
    let (ver, _) = DxfReader::check_version(Cursor::new(partial.as_bytes())).unwrap();
    assert_eq!(ver, DxfVersion::AutoCad2018);
}
