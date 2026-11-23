use std::f64::consts::FRAC_PI_2;

use netdxf::units::{
    CivilDateTime, DxfAngularValueFormat, DxfDateTimeFormat, DxfValueFormat,
};

#[test]
fn value_format_examples_and_validation() {
    let examples: [(&str, f64, &str); 20] = [
        ("%lu1%pr4", 17.5, "1.7500E+01"),
        ("%lu2%pr2", 17.5, "17.50"),
        ("%lu3%pr2", 17.5, "1'-5.50\""),
        ("%lu4%pr2", 17.5, "1'-5 1/2\""),
        ("%lu5%pr2", 17.5, "17 1/2"),
        ("%lu5%pr8", 0.5, "1/2"),
        ("%lu2%pr2%ps[,%]", 35.0, "35.00%"),
        ("%lu2%pr2%ps[$,]", 100.0, "$100.00"),
        ("%lu2%pr2%ct8[1000]%th44", 12.25, "12,250.00"),
        ("%lu2%pr2%ds44%th46", 12345.5, "12.345,50"),
        ("%lu2%pr4%zs12", 0.5, ".5"),
        ("%lu2%pr4%zs4", -0.5, "-.5000"),
        ("%lu2%pr4%zs8", 12.5, "12.5"),
        ("%lu2%pr2%zs12", 0.0, "0"),
        ("%lu2%pr2", -0.001, "0.00"),
        ("%lu2%pr1", -0.25, "-0.3"),
        ("%lu2%pr0", 2.5, "3"),
        ("%lu2%pr0", -2.5, "-3"),
        ("%lu1%pr2", 0.00125, "1.25E-03"),
        ("%pr2%lu2%ct8[-2]", 2.5, "-5.00"),
    ];
    for (expr, val, expected) in examples {
        let fmt = DxfValueFormat::parse(expr).unwrap_or_else(|e| panic!("parse {expr}: {e}"));
        assert_eq!(fmt.format_f64(val).unwrap(), expected, "expr={expr}");
    }

    assert_eq!(
        DxfValueFormat::parse("%ps[Ω,% done]")
            .unwrap()
            .format_str("text")
            .unwrap(),
        "Ωtext% done"
    );

    for bad in [
        "%",
        "%lu",
        "%lu2%pr",
        "%lu2%pr2%lu2",
        "%lu6%pr2",
        "%lu2%pr9",
        "%pr2",
        "%lu2",
        "%au0%pr2",
        "%zz1",
        "%lu2%pr2junk",
        "%lu2%pr2%ps[a,b,c]",
        "%ps[a,[b]]",
        "%ps[a,b",
        "%lu2%pr2%ct8[NaN]",
        "%lu2%pr2%ct8[Infinity]",
        "%lu2%pr2%ct7[1]",
        "%lu2%pr2%ct8[ 1]",
        "%lu2%pr2%ds44%th44",
        "%lu5%pr2%ds44",
        "%lu2%pr2%zs1",
        "not a format",
        "\0",
    ] {
        assert!(DxfValueFormat::parse(bad).is_err(), "should reject: {bad:?}");
    }
    let f = DxfValueFormat::parse("%lu2%pr2").unwrap();
    assert!(f.format_f64(f64::NAN).is_err());
    assert!(f.format_f64(f64::INFINITY).is_err());
    assert!(f.format_str("3").is_err());
}

#[test]
fn angular_and_datetime_format_conformance() {
    assert_eq!(
        DxfAngularValueFormat::parse("%au0%pr2")
            .unwrap()
            .format_radians(FRAC_PI_2)
            .unwrap(),
        "90.00"
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%au1%pr4")
            .unwrap()
            .format_radians(FRAC_PI_2)
            .unwrap(),
        "90°0'0\""
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%au2%pr2")
            .unwrap()
            .format_radians(FRAC_PI_2)
            .unwrap(),
        "100.00"
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%au3%pr2")
            .unwrap()
            .format_radians(FRAC_PI_2)
            .unwrap(),
        "1.57"
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%au4%pr4")
            .unwrap()
            .format_radians(FRAC_PI_2)
            .unwrap(),
        "N 0°0'0\" E"
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%ps[angle=, rad]%au3%pr2%ds44%zs8")
            .unwrap()
            .format_radians(1.5)
            .unwrap(),
        "angle=1,5 rad"
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%au3%pr2")
            .unwrap()
            .format_radians(0.125)
            .unwrap(),
        "0.12"
    );
    assert_eq!(
        DxfAngularValueFormat::parse("%au3%pr2%zs12")
            .unwrap()
            .format_radians(0.5)
            .unwrap(),
        ".5"
    );

    let clock = CivilDateTime::new(2026, 9, 17, 13, 5, 9);
    assert_eq!(
        DxfDateTimeFormat::parse("yyyy-MM-dd HH:mm:ss")
            .unwrap()
            .format(clock),
        "2026-09-17 13:05:09"
    );
    assert_eq!(
        DxfDateTimeFormat::parse("M MM MMM MMMM")
            .unwrap()
            .format(clock),
        "9 09 Sep September"
    );
    assert_eq!(
        DxfDateTimeFormat::parse("h hh H HH m mm s ss t tt")
            .unwrap()
            .format(clock),
        "1 01 13 13 5 05 9 09 P PM"
    );
    assert_eq!(
        DxfDateTimeFormat::parse("'day:' d 'don''t' ''\\d'' Ω")
            .unwrap()
            .format(clock),
        "day: 17 don't 'd' Ω"
    );
    assert!(DxfDateTimeFormat::parse("'open").is_err());
    assert!(DxfDateTimeFormat::parse("bad\0mask").is_err());
    assert!(DxfDateTimeFormat::parse("\\").is_err());
}
