#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// Standard entity projector handling core DXF shadow entities (Line, Circle, Arc, Polyline, Text, etc.).
#[derive(Debug, Default)]
pub struct StandardEntityProjector;

impl IEntityProjector for StandardEntityProjector {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        let reader = snapshot.read(entity_id.clone());
        let entity_type = reader.type_name();
        match entity_type {
            "Line" => {
                let start = reader.field("StartPoint").vector(Vec3::ZERO);
                let end = reader.field("EndPoint").vector(Vec3::ZERO);
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![Curve3::Line(crate::curves::LineCurve { a: start, b: end })],
                        closed: false,
                    }],
                    ..Default::default()
                })
            }
            "Circle" => {
                let center = reader.field("Center").vector(Vec3::ZERO);
                let radius = reader.field("Radius").number(0.0);
                if radius <= 0.0 {
                    return None;
                }
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![crate::curves::CurveSampler::circle(center, radius)],
                        closed: true,
                    }],
                    ..Default::default()
                })
            }
            "Arc" => {
                let center = reader.field("Center").vector(Vec3::ZERO);
                let radius = reader.field("Radius").number(0.0);
                if radius <= 0.0 {
                    return None;
                }
                let start_deg = reader.field("StartAngle").number(0.0);
                let end_deg = reader.field("EndAngle").number(360.0);
                let mut sweep_deg = end_deg - start_deg;
                if sweep_deg <= 0.0 {
                    sweep_deg += 360.0;
                }
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![Curve3::Ellipse(crate::curves::EllipseCurve {
                            center,
                            u: Vec3::new(radius, 0.0, 0.0),
                            v: Vec3::new(0.0, radius, 0.0),
                            start_angle: start_deg.to_radians(),
                            sweep: sweep_deg.to_radians(),
                        })],
                        closed: false,
                    }],
                    ..Default::default()
                })
            }
            "Ellipse" => {
                let center = reader.field("Center").vector(Vec3::ZERO);
                let major = reader.field("MajorAxis").number(0.0) * 0.5;
                let minor = reader.field("MinorAxis").number(0.0) * 0.5;
                if major <= 0.0 || minor <= 0.0 {
                    return None;
                }
                let rot = reader.field("Rotation").number(0.0).to_radians();
                let (s, c) = rot.sin_cos();
                let start_deg = reader.field("StartAngle").number(0.0);
                let end_deg = reader.field("EndAngle").number(360.0);
                let mut sweep_deg = end_deg - start_deg;
                if sweep_deg <= 0.0 {
                    sweep_deg += 360.0;
                }
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![Curve3::Ellipse(crate::curves::EllipseCurve {
                            center,
                            u: Vec3::xy(major * c, major * s),
                            v: Vec3::xy(-minor * s, minor * c),
                            start_angle: start_deg.to_radians(),
                            sweep: sweep_deg.to_radians(),
                        })],
                        closed: (sweep_deg - 360.0).abs() < 1e-6,
                    }],
                    ..Default::default()
                })
            }
            "Polyline2D" => {
                let items = reader.field("Vertexes").items();
                let closed = reader.field("IsClosed").boolean(false);
                let elev = reader.field("Elevation").number(0.0);
                if items.len() < 2 {
                    return Some(CadGeometry::default());
                }
                let mut curves = Vec::new();
                let seg_count = if closed { items.len() } else { items.len() - 1 };
                for i in 0..seg_count {
                    let a_r = &items[i];
                    let b_r = &items[(i + 1) % items.len()];
                    let p0 = a_r.field("Position").vector(Vec3::ZERO);
                    let p1 = b_r.field("Position").vector(Vec3::ZERO);
                    let a = Vec3::new(p0.x, p0.y, elev);
                    let b = Vec3::new(p1.x, p1.y, elev);
                    let bulge = a_r.field("Bulge").number(0.0);
                    if bulge.abs() < 1e-9 {
                        curves.push(Curve3::Line(crate::curves::LineCurve { a, b }));
                    } else {
                        let chord = Vec3::distance(a, b);
                        if chord > 1e-12 {
                            let theta = 4.0 * bulge.atan();
                            let half = theta * 0.5;
                            let radius = (chord * 0.5) / half.sin().abs();
                            let mid = (a + b) * 0.5;
                            let dir = (b - a) / chord;
                            let perp = Vec3::xy(-dir.y, dir.x);
                            let sag_offset = radius * half.cos() * bulge.signum();
                            let center = Vec3::new(
                                mid.x + perp.x * sag_offset,
                                mid.y + perp.y * sag_offset,
                                elev,
                            );
                            let start_angle = (a.y - center.y).atan2(a.x - center.x);
                            curves.push(Curve3::Ellipse(crate::curves::EllipseCurve {
                                center,
                                u: Vec3::new(radius, 0.0, 0.0),
                                v: Vec3::new(0.0, radius, 0.0),
                                start_angle,
                                sweep: theta,
                            }));
                        }
                    }
                }
                Some(CadGeometry {
                    contours: vec![CadContour { curves, closed }],
                    ..Default::default()
                })
            }
            "Polyline3D" => {
                let items = reader.field("Vertexes").items();
                let closed = reader.field("IsClosed").boolean(false);
                if items.len() < 2 {
                    return Some(CadGeometry::default());
                }
                let mut curves = Vec::new();
                let seg_count = if closed { items.len() } else { items.len() - 1 };
                for i in 0..seg_count {
                    let a = items[i].vector(Vec3::ZERO);
                    let b = items[(i + 1) % items.len()].vector(Vec3::ZERO);
                    curves.push(Curve3::Line(crate::curves::LineCurve { a, b }));
                }
                Some(CadGeometry {
                    contours: vec![CadContour { curves, closed }],
                    ..Default::default()
                })
            }
            "Solid" | "Face3D" => {
                let v0 = reader.field("FirstVertex").vector(Vec3::ZERO);
                let v1 = reader.field("SecondVertex").vector(Vec3::ZERO);
                let v2 = reader.field("ThirdVertex").vector(Vec3::ZERO);
                let v3 = reader.field("FourthVertex").vector(Vec3::ZERO);
                let curves = vec![
                    Curve3::Line(crate::curves::LineCurve { a: v0, b: v1 }),
                    Curve3::Line(crate::curves::LineCurve { a: v1, b: v2 }),
                    Curve3::Line(crate::curves::LineCurve { a: v2, b: v3 }),
                    Curve3::Line(crate::curves::LineCurve { a: v3, b: v0 }),
                ];
                let contour = CadContour {
                    curves,
                    closed: true,
                };
                Some(CadGeometry {
                    contours: vec![contour.clone()],
                    filled: entity_type == "Solid",
                    regions: if entity_type == "Solid" {
                        vec![CadRegion {
                            contours: vec![contour],
                            fill_rule: CadFillRule::EvenOdd,
                            argb: None,
                            gradient: None,
                        }]
                    } else {
                        Vec::new()
                    },
                    ..Default::default()
                })
            }
            "Text" | "MText" => {
                let origin = reader.field("Position").vector(Vec3::ZERO);
                let height = reader.field("Height").number(2.5);
                let rot = reader.field("Rotation").number(0.0).to_radians();
                let (s, c) = rot.sin_cos();
                Some(CadGeometry {
                    texts: vec![CadText {
                        content: reader.field("Value").text().to_string(),
                        origin,
                        x_axis: Vec3::xy(c, s),
                        y_axis: Vec3::xy(-s, c),
                        height,
                        width_factor: 1.0,
                        oblique_angle: 0.0,
                        font: "Arial".into(),
                        is_mtext: entity_type == "MText",
                        line_spacing: CadLineSpacing::default(),
                    }],
                    points: vec![origin],
                    ..Default::default()
                })
            }
            "Point" => {
                let pos = reader.field("Position").vector(Vec3::ZERO);
                Some(CadGeometry {
                    points: vec![pos],
                    ..Default::default()
                })
            }
            _ => Some(CadGeometry::default()),
        }
    }
}

impl ICacheableEntityProjector for StandardEntityProjector {
    fn projector_key(&self) -> &str {
        "standard"
    }
}

