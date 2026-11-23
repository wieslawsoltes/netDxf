use std::io::{Cursor, Seek, SeekFrom};

use netdxf::entities::{EntityObject, Line};
use netdxf::header::{DxfVersion, HeaderVariable, HeaderVariableValue};
use netdxf::io::{DxfReader, BINARY_DXF_SENTINEL};
use netdxf::objects::{DxfGeoData, DxfStoredSunStudy, DxfSun, LayerState, LayerStateProperties};
use netdxf::tables::{Layer, TableObject, UCS};
use netdxf::units::DrawingUnits;
use netdxf::{AciColor, DxfDocument, Transparency, Vector2, Vector3};

const SUPPORTED_VERSIONS: [DxfVersion; 6] = [
    DxfVersion::AutoCad2000,
    DxfVersion::AutoCad2004,
    DxfVersion::AutoCad2007,
    DxfVersion::AutoCad2010,
    DxfVersion::AutoCad2013,
    DxfVersion::AutoCad2018,
];

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

            let line = doc
                .entities
                .iter()
                .find_map(|e| match e {
                    EntityObject::Line(l) => Some(l),
                    _ => None,
                })
                .expect("following line entity");
            assert_eq!(line.start_point, Vector3::new(1.0, 2.0, 3.0));
            assert_eq!(line.end_point, Vector3::new(4.0, 5.0, 6.0));
        }
    }
}

#[test]
fn header_probe_conformance() {
    // Truncated binary sentinel
    for len in 0..BINARY_DXF_SENTINEL.len() {
        let (ver, is_bin) =
            DxfReader::check_version(Cursor::new(&BINARY_DXF_SENTINEL[..len])).unwrap();
        assert_eq!(ver, DxfVersion::Unknown);
        assert!(!is_bin);
    }

    // Single-bit corrupt binary sentinel
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

    // Non-zero stream offset & position restoration
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

    // Headerless and malformed text files
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

    // Comment control words must not confuse probe
    for word in ["EOF", "HEADER", "ENDSEC", "SECTION", "$ACADVER"] {
        let text = format!(
            "999\n{word}\n0\nSECTION\n999\n{word}\n2\nHEADER\n999\n{word}\n9\n$ACADVER\n999\n{word}\n1\nAC1032\n0\nENDSEC\n0\nEOF\n"
        );
        let (ver, is_bin) = DxfReader::check_version(Cursor::new(text.as_bytes())).unwrap();
        assert_eq!(ver, DxfVersion::AutoCad2018);
        assert!(!is_bin);
    }

    // Skip CLASSES section before HEADER
    let after_classes = "0\nSECTION\n2\nCLASSES\n0\nCLASS\n1\nHEADER\n90\n0\n280\n0\n281\n0\n0\nENDSEC\n0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n0\nENDSEC\n0\nEOF\n";
    let (ver, _) = DxfReader::check_version(Cursor::new(after_classes.as_bytes())).unwrap();
    assert_eq!(ver, DxfVersion::AutoCad2018);

    // Not full file validation: garbage after $ACADVER still probes version
    let partial =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\ngarbage-after-selected-variable\n";
    let (ver, _) = DxfReader::check_version(Cursor::new(partial.as_bytes())).unwrap();
    assert_eq!(ver, DxfVersion::AutoCad2018);
}

#[test]
fn custom_header_unicode_and_scalars_roundtrip() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let expected_str = if version.uses_utf8() {
                "Zażółć 東京 Ω"
            } else {
                "ordinary ASCII \\ path"
            };
            doc.drawing_variables.custom_variables.insert(
                "$PROJECTNAME".into(),
                HeaderVariable::new(
                    "$PROJECTNAME",
                    1,
                    HeaderVariableValue::String(expected_str.into()),
                ),
            );
            doc.drawing_variables.custom_variables.insert(
                "$USERI1".into(),
                HeaderVariable::new("$USERI1", 70, HeaderVariableValue::Int16(-123)),
            );
            doc.drawing_variables.custom_variables.insert(
                "$USERR1".into(),
                HeaderVariable::new("$USERR1", 40, HeaderVariableValue::Float(1.23456789)),
            );
            doc.drawing_variables.custom_variables.insert(
                "$EXTMIN".into(),
                HeaderVariable::new(
                    "$EXTMIN",
                    30,
                    HeaderVariableValue::Vector3(Vector3::new(-10.0, -20.0, -30.0)),
                ),
            );

            let bytes = doc.save_to_bytes(binary).expect("save custom header");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load custom header");
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$PROJECTNAME")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::String(expected_str.into()))
            );
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$USERI1")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::Int16(-123))
            );
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$USERR1")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::Float(1.23456789))
            );
            assert_eq!(
                loaded
                    .drawing_variables
                    .custom_variables
                    .get("$EXTMIN")
                    .map(|v| &v.value),
                Some(&HeaderVariableValue::Vector3(Vector3::new(
                    -10.0, -20.0, -30.0
                )))
            );
        }
    }
}

#[test]
fn ucs_and_layer_state_conformance() {
    for &version in &SUPPORTED_VERSIONS {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let mut ucs = UCS::from_origin_axes(
                "SURVEY_BASE",
                Vector3::new(100.0, 200.0, 300.0),
                Vector3::UNIT_X,
                Vector3::UNIT_Y,
            )
            .unwrap();
            ucs.elevation = -8.5;
            doc.ucss.add(ucs);

            let mut layer = Layer::new("STATE_LAYER").unwrap();
            layer.color = AciColor::from_rgb(10, 20, 30);
            layer.transparency = Transparency::new(40).unwrap();
            layer.is_frozen = true;
            layer.is_locked = true;
            let snapshot = LayerStateProperties::from(&layer);
            doc.layers.add(layer);

            let mut state = LayerState::new("ARCH_STATE").unwrap();
            state.properties.insert("STATE_LAYER".into(), snapshot);
            assert_eq!(state.name(), "ARCH_STATE");
            assert!(state.properties.get("STATE_LAYER").unwrap().is_frozen);

            let bytes = doc.save_to_bytes(binary).expect("save UCS");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load UCS");
            let loaded_ucs = loaded.ucss.get("SURVEY_BASE").expect("SURVEY_BASE UCS");
            assert_eq!(loaded_ucs.origin, Vector3::new(100.0, 200.0, 300.0));
            assert!((loaded_ucs.elevation - (-8.5)).abs() < 1e-12);
        }
    }
}

#[test]
fn geodata_chunked_definition_roundtrip() {
    let long_def = format!("{}{}{}", "x".repeat(252), "Zolc_UTF8_", "z".repeat(600));
    for &version in &[
        DxfVersion::AutoCad2010,
        DxfVersion::AutoCad2013,
        DxfVersion::AutoCad2018,
    ] {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let geo = DxfGeoData {
                design_coordinates_type: 1,
                design_point: Vector3::new(1.25, -2.5, 7.75),
                reference_point: Vector3::new(11.5, 48.25, 100.125),
                horizontal_unit_scale: 0.3048,
                vertical_unit_scale: 0.0254,
                horizontal_units: 2, // Feet
                vertical_units: 1,   // Inches
                north_direction: Vector2::new(3.0, 4.0),
                up_direction: Vector3::new(2.0, 3.0, 4.0),
                scale_estimation_method: 4,
                user_scale_factor: 0.9996,
                coordinate_system_definition: long_def.clone(),
                geo_rss_tag: "metadata".into(),
                observation_from_tag: "origin Żółć".into(),
                observation_to_tag: "target 東京".into(),
                observation_coverage_tag: "survey bounds".into(),
                ..Default::default()
            };
            doc.geo_data = Some(geo);

            let bytes = doc.save_to_bytes(binary).expect("save GEODATA");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load GEODATA");
            let d = loaded.geo_data.as_ref().expect("loaded GEODATA");
            assert_eq!(d.coordinate_system_definition, long_def);
            assert_eq!(d.design_point, Vector3::new(1.25, -2.5, 7.75));
            assert_eq!(d.reference_point, Vector3::new(11.5, 48.25, 100.125));
            assert_eq!(d.north_direction, Vector2::new(3.0, 4.0));
            assert_eq!(d.up_direction, Vector3::new(2.0, 3.0, 4.0));
            assert_eq!(d.horizontal_units, 2);
            assert_eq!(d.vertical_units, 1);
            assert_eq!(d.observation_to_tag, "target 東京");
        }
    }
}

#[test]
fn sun_and_sun_study_conformance_all_r2007_plus_versions() {
    for &version in &[
        DxfVersion::AutoCad2007,
        DxfVersion::AutoCad2010,
        DxfVersion::AutoCad2013,
        DxfVersion::AutoCad2018,
    ] {
        for &binary in &[false, true] {
            let mut doc = DxfDocument::with_version(version);
            let sun = DxfSun {
                status: true,
                color: AciColor::from_rgb(0x12, 0x34, 0x56),
                intensity: -2.125,
                shadows: false,
                julian_day: 2_455_826,
                time_milliseconds: 54_000_000,
                daylight_savings: true,
                shadow_type: 2,
                shadow_map_size: 4096,
                shadow_softness: 255,
                ..Default::default()
            };
            doc.sun = Some(sun);

            let study = DxfStoredSunStudy {
                name: "EquinoxStudy".into(),
                description: "Solar path study".into(),
                julian_dates: vec![(2_455_826, 36_000_000), (2_455_826, 54_000_000)],
                start_time_seconds: 28_800,
                end_time_seconds: 64_800,
                interval_seconds: 3_600,
                ..Default::default()
            };
            doc.sun_studies.push(study);

            let bytes = doc.save_to_bytes(binary).expect("save SUN");
            let loaded = DxfDocument::load_from_bytes(&bytes).expect("load SUN");
            let s = loaded.sun.as_ref().expect("loaded SUN");
            assert!(s.status);
            assert_eq!(s.intensity, -2.125);
            assert!(!s.shadows);
            assert_eq!(s.julian_day, 2_455_826);
            assert_eq!(s.time_milliseconds, 54_000_000);
            assert!(s.daylight_savings);
            assert_eq!(s.shadow_type, 2);
            assert_eq!(s.shadow_map_size, 4096);
            assert_eq!(s.shadow_softness, 255);

            assert_eq!(loaded.sun_studies.len(), 1);
            assert_eq!(loaded.sun_studies[0].name, "EquinoxStudy");
            assert_eq!(loaded.sun_studies[0].julian_dates.len(), 2);
        }
    }
}
