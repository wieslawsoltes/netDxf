use std::io::Cursor;

use netdxf::entities::{EntityObject, Line};
use netdxf::header::DxfVersion;
use netdxf::io::DxfReader;
use netdxf::units::DrawingUnits;
use netdxf::{DxfDocument, Vector3};

use super::helpers::SUPPORTED_VERSIONS;

fn header_comments_dxf(version: DxfVersion, gap: i32, crlf: bool) -> String {
    let nl = if crlf { "\r\n" } else { "\n" };
    let pairs: Vec<(i16, String)> = vec![
        (0, "SECTION".into()),
        (2, "HEADER".into()),
        (9, "$ACADVER".into()),
        (1, version.as_code().into()),
        (9, "$DWGCODEPAGE".into()),
        (3, "ANSI_1252".into()),
        (9, "$ANGBASE".into()),
        (50, "15.0".into()),
        (9, "$INSBASE".into()),
        (10, "1.25".into()),
        (20, "-2.5".into()),
        (30, "3.75".into()),
        (9, "$UCSORG".into()),
        (10, "10.0".into()),
        (20, "20.0".into()),
        (30, "30.0".into()),
        (9, "$UCSXDIR".into()),
        (10, "1.0".into()),
        (20, "0.0".into()),
        (30, "0.0".into()),
        (9, "$UCSYDIR".into()),
        (10, "0.0".into()),
        (20, "1.0".into()),
        (30, "0.0".into()),
        (9, "$PROJECTNAME".into()),
        (1, "ENDSEC".into()),
        (9, "$HYPERLINKBASE".into()),
        (1, "EOF".into()),
        (9, "$USERI1".into()),
        (70, "-12".into()),
        (9, "$USERR1".into()),
        (40, "1.25".into()),
        (9, "$LASTSAVEDBY".into()),
        (1, "comments test".into()),
        (9, "$INSUNITS".into()),
        (70, "4".into()),
    ];
    let mut out = String::new();
    for (i, (code, val)) in pairs.iter().enumerate() {
        out.push_str(&format!("{code}{nl}{val}{nl}"));
        if i >= 1 && (i as i32 == gap || gap == -1) {
            for comment in ["", "ENDSEC", "EOF", "$ACADVER", "AC1009", "999"] {
                out.push_str(&format!("999{nl}{comment}{nl}"));
            }
        }
    }
    out.push_str(&format!("0{nl}ENDSEC{nl}0{nl}SECTION{nl}2{nl}ENTITIES{nl}"));
    out.push_str(&format!(
        "0{nl}LINE{nl}5{nl}200{nl}100{nl}AcDbEntity{nl}8{nl}0{nl}100{nl}AcDbLine{nl}"
    ));
    out.push_str(&format!(
        "10{nl}1.0{nl}20{nl}2.0{nl}30{nl}3.0{nl}11{nl}4.0{nl}21{nl}5.0{nl}31{nl}6.0{nl}"
    ));
    out.push_str(&format!("0{nl}ENDSEC{nl}0{nl}EOF{nl}"));
    out
}

#[test]
fn header_comments_interleaved_all_versions() {
    for &version in &SUPPORTED_VERSIONS {
        for &(gap, crlf) in &[(-1, false), (-1, true), (-2, false), (2, false), (8, false)] {
            let text = header_comments_dxf(version, gap, crlf);
            let bytes = text.as_bytes();
            let (probed, is_bin) = DxfReader::check_version(Cursor::new(bytes)).unwrap();
            assert_eq!(probed, version);
            assert!(!is_bin);

            let doc = DxfDocument::load_from_bytes(bytes).expect("load commented header");
            assert_eq!(doc.drawing_variables.acad_ver, version);
            assert!((doc.drawing_variables.angbase - 15.0).abs() < 1e-12);
            assert_eq!(doc.drawing_variables.ins_base, Vector3::new(1.25, -2.5, 3.75));
            assert_eq!(doc.drawing_variables.ucs_org, Vector3::new(10.0, 20.0, 30.0));
            assert_eq!(doc.drawing_variables.ucs_x_dir, Vector3::UNIT_X);
            assert_eq!(doc.drawing_variables.ucs_y_dir, Vector3::UNIT_Y);
            assert_eq!(doc.drawing_variables.last_saved_by, "comments test");
            assert_eq!(doc.drawing_variables.ins_units, DrawingUnits::Millimeters);

            let line: &Line = doc
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) => Some(l.as_ref()),
                    _ => None,
                })
                .expect("following line entity");
            assert_eq!(line.start_point, Vector3::new(1.0, 2.0, 3.0));
            assert_eq!(line.end_point, Vector3::new(4.0, 5.0, 6.0));
        }
    }
}
