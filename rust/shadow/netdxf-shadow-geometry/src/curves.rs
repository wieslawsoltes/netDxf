#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

/// 3D curve algebra (Line, Ellipse/Arc, NURBS, WidthBoundary, Reversed).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub enum Curve3 {
    Line(LineCurve),
    Ellipse(EllipseCurve),
    Nurbs(NurbsCurve),
    WidthBoundary(Box<WidthBoundaryCurve>),
    Reversed(Box<ReversedCurve>),
}

impl Curve3 {
    pub fn evaluate(&self, t: f64) -> Vec3 {
        match self {
            Self::Line(c) => c.evaluate(t),
            Self::Ellipse(c) => c.evaluate(t),
            Self::Nurbs(c) => c.evaluate(t),
            Self::WidthBoundary(c) => c.centerline.evaluate(t),
            Self::Reversed(c) => c.curve.evaluate(1.0 - t),
        }
    }

    pub fn transform(&self, m: Affine3) -> Self {
        match self {
            Self::Line(c) => Self::Line(LineCurve {
                a: m.point(c.a),
                b: m.point(c.b),
            }),
            Self::Ellipse(c) => Self::Ellipse(EllipseCurve {
                center: m.point(c.center),
                u: m.direction(c.u),
                v: m.direction(c.v),
                start_angle: c.start_angle,
                sweep: c.sweep,
            }),
            Self::Nurbs(c) => Self::Nurbs(NurbsCurve {
                degree: c.degree,
                control_points: c.control_points.iter().map(|&p| m.point(p)).collect(),
                weights: c.weights.clone(),
                knots: c.knots.clone(),
            }),
            Self::WidthBoundary(c) => Self::WidthBoundary(Box::new(WidthBoundaryCurve {
                centerline: c.centerline.transform(m),
                start_width: c.start_width,
                end_width: c.end_width,
                side: c.side,
            })),
            Self::Reversed(c) => Self::Reversed(Box::new(ReversedCurve {
                curve: c.curve.transform(m),
            })),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LineCurve {
    pub a: Vec3,
    pub b: Vec3,
}

impl LineCurve {
    pub fn evaluate(self, t: f64) -> Vec3 {
        self.a + (self.b - self.a) * t
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct EllipseCurve {
    pub center: Vec3,
    pub u: Vec3,
    pub v: Vec3,
    pub start_angle: f64,
    pub sweep: f64,
}

impl EllipseCurve {
    pub fn evaluate(self, t: f64) -> Vec3 {
        let angle = self.start_angle + self.sweep * t;
        let (s, c) = angle.sin_cos();
        self.center + self.u * c + self.v * s
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct NurbsCurve {
    pub degree: usize,
    pub control_points: Vec<Vec3>,
    pub weights: Vec<f64>,
    pub knots: Vec<f64>,
}

impl NurbsCurve {
    pub fn evaluate(&self, t: f64) -> Vec3 {
        if self.control_points.is_empty() {
            return Vec3::ZERO;
        }
        if self.control_points.len() == 1 {
            return self.control_points[0];
        }
        let u = t.clamp(0.0, 1.0);
        let idx = ((u * (self.control_points.len() - 1) as f64).floor() as usize)
            .min(self.control_points.len() - 2);
        let frac = u * (self.control_points.len() - 1) as f64 - idx as f64;
        let a = self.control_points[idx];
        let b = self.control_points[idx + 1];
        a + (b - a) * frac
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct SamplingOptions {
    pub tolerance: f64,
    pub maximum_points: usize,
    pub maximum_depth: usize,
}

impl Default for SamplingOptions {
    fn default() -> Self {
        Self {
            tolerance: 0.05,
            maximum_points: 262_144,
            maximum_depth: 20,
        }
    }
}

/// Adaptive curve tessellator for [`Curve3`].
pub struct CurveSampler;

impl CurveSampler {
    pub fn sample(curve: &Curve3, options: &SamplingOptions) -> Vec<Vec3> {
        match curve {
            Curve3::Line(l) => vec![l.a, l.b],
            Curve3::Ellipse(e) => {
                let radius = e.u.length().max(e.v.length()).max(1e-9);
                let tol = options.tolerance.max(1e-9);
                let cos_half = (1.0 - tol / radius).clamp(-1.0, 1.0);
                let max_angle = (2.0 * cos_half.acos()).max(1e-3);
                let segments = ((e.sweep.abs() / max_angle).ceil() as usize)
                    .clamp(4, options.maximum_points.saturating_sub(1).max(4));
                let mut pts = Vec::with_capacity(segments + 1);
                for i in 0..=segments {
                    pts.push(e.evaluate(i as f64 / segments as f64));
                }
                pts
            }
            Curve3::Nurbs(n) => {
                let segments = (n.control_points.len().saturating_mul(8))
                    .clamp(8, options.maximum_points.saturating_sub(1).max(8));
                let mut pts = Vec::with_capacity(segments + 1);
                for i in 0..=segments {
                    pts.push(n.evaluate(i as f64 / segments as f64));
                }
                pts
            }
            Curve3::WidthBoundary(w) => Self::sample(&w.centerline, options),
            Curve3::Reversed(r) => {
                let mut pts = Self::sample(&r.curve, options);
                pts.reverse();
                pts
            }
        }
    }

    pub fn circle(center: Vec3, radius: f64) -> Curve3 {
        Curve3::Ellipse(EllipseCurve {
            center,
            u: Vec3::new(radius, 0.0, 0.0),
            v: Vec3::new(0.0, radius, 0.0),
            start_angle: 0.0,
            sweep: TAU,
        })
    }
}

