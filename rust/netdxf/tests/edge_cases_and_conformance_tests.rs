use netdxf::entities::*;
use netdxf::tables::{is_valid_name, Layer, Linetype, TextStyle};
use netdxf::{
    BezierCurveCubic, DxfDocument, DxfError, MathHelper, Vector2, Vector3,
};

#[test]
fn table_name_validation_and_reserved_protection() {
    assert!(is_valid_name("MyLayer_1"));
    assert!(!is_valid_name(""));
    assert!(!is_valid_name("   "));
    for bad in ["a/b", "a\\b", "a:b", "a*b", "a?b", "a<b", "a>b", "a|b", "a;b", "a,b", "a=b"] {
        assert!(!is_valid_name(bad), "should reject {bad}");
        assert!(Layer::new(bad).is_err());
    }

    let mut doc = DxfDocument::new();
    // Reserved table objects cannot be removed
    assert!(matches!(
        doc.layers.remove(Layer::DEFAULT_NAME),
        Err(DxfError::CannotRemove { .. })
    ));
    assert!(matches!(
        doc.linetypes.remove(Linetype::CONTINUOUS_NAME),
        Err(DxfError::CannotRemove { .. })
    ));
    assert!(matches!(
        doc.text_styles.remove(TextStyle::DEFAULT_NAME),
        Err(DxfError::CannotRemove { .. })
    ));

    // User layer can be added and removed case-insensitively
    doc.layers.add(Layer::new("TempLayer").unwrap());
    assert!(doc.layers.contains("templayer"));
    assert!(doc.layers.remove("TEMPLAYER").is_ok());
    assert!(!doc.layers.contains("TempLayer"));
}

#[test]
fn spline_matches_cubic_bezier_exactly() {
    let p0 = Vector3::new(0.0, 0.0, 0.0);
    let p1 = Vector3::new(2.0, 8.0, 1.0);
    let p2 = Vector3::new(8.0, 8.0, -1.0);
    let p3 = Vector3::new(10.0, 0.0, 0.0);

    let bezier = BezierCurveCubic::new(p0, p1, p2, p3);
    let spline = Spline::from_control_points(vec![p0, p1, p2, p3], 3, false);

    for i in 0..=20 {
        let t = i as f64 / 20.0;
        let b = bezier.calculate_point(t);
        let s = spline.evaluate(t).expect("spline evaluate failed");
        assert!(
            b.equals_with(s, 1e-10),
            "mismatch at t={t}: bezier={b}, spline={s}"
        );
    }
}

#[test]
fn polyline2d_reverse_negates_bulges_and_swaps_widths() {
    let mut v0 = Polyline2DVertex::with_bulge(0.0, 0.0, 0.5);
    v0.start_width = 1.0;
    v0.end_width = 2.0;
    let v1 = Polyline2DVertex::with_bulge(10.0, 0.0, -0.25);
    let v2 = Polyline2DVertex::new(10.0, 10.0);

    let mut pl = Polyline2D::new(vec![v0, v1, v2], false);
    pl.reverse();

    assert_eq!(pl.vertexes[0].position, Vector2::new(10.0, 10.0));
    assert!(MathHelper::is_equal(pl.vertexes[0].bulge, 0.25));
    assert_eq!(pl.vertexes[1].position, Vector2::new(10.0, 0.0));
    assert!(MathHelper::is_equal(pl.vertexes[1].bulge, -0.5));
    assert_eq!(pl.vertexes[2].position, Vector2::new(0.0, 0.0));
    assert!(MathHelper::is_equal(pl.vertexes[2].start_width, 2.0));
    assert!(MathHelper::is_equal(pl.vertexes[2].end_width, 1.0));
}

#[test]
fn text_alignment_all_variants_roundtrip() {
    let all = [
        TextAlignment::TopLeft,
        TextAlignment::TopCenter,
        TextAlignment::TopRight,
        TextAlignment::MiddleLeft,
        TextAlignment::MiddleCenter,
        TextAlignment::MiddleRight,
        TextAlignment::BottomLeft,
        TextAlignment::BottomCenter,
        TextAlignment::BottomRight,
        TextAlignment::BaselineLeft,
        TextAlignment::BaselineCenter,
        TextAlignment::BaselineRight,
        TextAlignment::Aligned,
        TextAlignment::Middle,
        TextAlignment::Fit,
    ];
    for a in all {
        let (h, v) = a.to_dxf_codes();
        assert_eq!(TextAlignment::from_dxf_codes(h, v), a);
    }
}

#[test]
fn malformed_and_truncated_inputs_return_err_without_panic() {
    assert!(DxfDocument::load_from_bytes(b"").is_err());
    assert!(DxfDocument::load_from_bytes(b"NOT_A_CODE\nVALUE\n").is_err());
    assert!(DxfDocument::load_from_bytes(b"0\n").is_err());
    assert!(DxfDocument::load_from_bytes(b"AutoCAD Binary DXF\r\n\x1a\0\xFF").is_err());
}
