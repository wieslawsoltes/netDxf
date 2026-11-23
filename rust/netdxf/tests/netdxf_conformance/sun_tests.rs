use netdxf::header::DxfVersion;
use netdxf::objects::{DxfStoredSunStudy, DxfSun};
use netdxf::{AciColor, DxfDocument};

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
