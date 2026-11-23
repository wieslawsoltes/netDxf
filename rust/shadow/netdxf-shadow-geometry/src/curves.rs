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

    #[inline]
    pub fn at(&self, t: f64) -> Vec3 {
        self.evaluate(t)
    }

    #[inline]
    pub fn start(&self) -> Vec3 {
        self.evaluate(0.0)
    }

    #[inline]
    pub fn end(&self) -> Vec3 {
        self.evaluate(1.0)
    }

    #[inline]
    pub fn closed(&self) -> bool {
        Vec3::distance(self.start(), self.end()) <= 1e-6
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
                frame: m * c.frame,
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

    #[inline]
    pub fn at(&self, t: f64) -> Vec3 {
        self.evaluate(t)
    }

    #[inline]
    pub fn transform(&self, m: Affine3) -> Self {
        Self {
            a: m.point(self.a),
            b: m.point(self.b),
        }
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

    #[inline]
    pub fn at(&self, t: f64) -> Vec3 {
        self.evaluate(t)
    }

    #[inline]
    pub fn closed(&self) -> bool {
        (self.sweep.abs() - TAU).abs() <= 1e-6
    }

    #[inline]
    pub fn transform(&self, m: Affine3) -> Self {
        Self {
            center: m.point(self.center),
            u: m.direction(self.u),
            v: m.direction(self.v),
            start_angle: self.start_angle,
            sweep: self.sweep,
        }
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
    pub fn controls(&self) -> &[Vec3] {
        &self.control_points
    }

    pub fn closed(&self) -> bool {
        if self.control_points.len() < 2 {
            return false;
        }
        let first = self.control_points[0];
        let last = self.control_points[self.control_points.len() - 1];
        Vec3::distance(first, last) <= 1e-6
    }

    pub fn domain_start(&self) -> f64 {
        let p = self.degree;
        if self.knots.len() > p {
            self.knots[p]
        } else {
            0.0
        }
    }

    pub fn domain_end(&self) -> f64 {
        let n = self.control_points.len();
        if n > 0 && self.knots.len() > n {
            self.knots[n]
        } else {
            1.0
        }
    }

    pub fn blend(&self, span: usize, u: f64) -> Vec<f64> {
        let p = self.degree;
        let mut basis = vec![0.0; p + 1];
        let mut left = vec![0.0; p + 1];
        let mut right = vec![0.0; p + 1];
        basis[0] = 1.0;
        for j in 1..=p {
            left[j] = u - self.knots[span + 1 - j];
            right[j] = self.knots[span + j] - u;
            let mut saved = 0.0;
            for r in 0..j {
                let denom = right[r + 1] + left[j - r];
                let temp = if denom.abs() > f64::EPSILON {
                    basis[r] / denom
                } else {
                    0.0
                };
                basis[r] = saved + right[r + 1] * temp;
                saved = left[j - r] * temp;
            }
            basis[j] = saved;
        }
        basis
    }

    /// Evaluates the rational NURBS curve at parameter `u` in `[domain_start(), domain_end()]` using Cox-de Boor.
    pub fn at(&self, u: f64) -> Vec3 {
        let n = self.control_points.len();
        if n == 0 {
            return Vec3::ZERO;
        }
        if n == 1 {
            return self.control_points[0];
        }
        let p = self.degree.min(n - 1);
        if self.knots.len() < n + p + 1 {
            // Fallback to clamped uniform evaluation if knot vector is incomplete
            let t = u.clamp(0.0, 1.0);
            let idx = ((t * (n - 1) as f64).floor() as usize).min(n - 2);
            let frac = t * (n - 1) as f64 - idx as f64;
            let a = self.control_points[idx];
            let b = self.control_points[idx + 1];
            return a + (b - a) * frac;
        }
        let u_min = self.knots[p];
        let u_max = self.knots[n];
        let u = u.clamp(u_min, u_max);
        let span = if (u - u_max).abs() <= f64::EPSILON {
            n - 1
        } else {
            let mut lo = p;
            let mut hi = n;
            let mut mid = (lo + hi) / 2;
            while u < self.knots[mid] || u >= self.knots[mid + 1] {
                if u < self.knots[mid] {
                    hi = mid;
                } else {
                    lo = mid;
                }
                mid = (lo + hi) / 2;
                if hi <= lo + 1 {
                    break;
                }
            }
            mid.clamp(p, n - 1)
        };
        let basis = self.blend(span, u);
        let mut numer = Vec3::ZERO;
        let mut denom = 0.0;
        for (j, &nj) in basis.iter().enumerate() {
            let ci = span + j - p;
            let w = self.weights.get(ci).copied().unwrap_or(1.0);
            let bw = nj * w;
            numer = numer + self.control_points[ci] * bw;
            denom += bw;
        }
        if denom.abs() > f64::EPSILON {
            numer * (1.0 / denom)
        } else {
            numer
        }
    }

    pub fn evaluate(&self, t: f64) -> Vec3 {
        let u0 = self.domain_start();
        let u1 = self.domain_end();
        let u = u0 + t.clamp(0.0, 1.0) * (u1 - u0);
        self.at(u)
    }

    pub fn transform(&self, affine: &Affine3) -> Self {
        Self {
            degree: self.degree,
            control_points: self.control_points.iter().map(|&p| affine.point(p)).collect(),
            weights: self.weights.clone(),
            knots: self.knots.clone(),
        }
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

