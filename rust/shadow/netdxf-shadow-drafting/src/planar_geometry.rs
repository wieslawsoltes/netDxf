#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Bounds2, ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DraftingArc {
    pub center: Vec3,
    pub radius: f64,
    pub start_angle: f64,
    pub end_angle: f64,
    pub start_degrees: f64,
    pub end_degrees: f64,
}

impl DraftingArc {
    pub fn new(center: Vec3, radius: f64, start_degrees: f64, end_degrees: f64) -> Self {
        Self {
            center,
            radius,
            start_angle: start_degrees,
            end_angle: end_degrees,
            start_degrees,
            end_degrees,
        }
    }
}

pub struct PlanarGeometry;

impl PlanarGeometry {
    pub fn cross(a: Vec3, b: Vec3) -> f64 {
        a.x * b.y - a.y * b.x
    }

    pub fn left(value: Vec3) -> Vec3 {
        Vec3::xy(-value.y, value.x)
    }

    pub fn degrees(value: Vec3) -> f64 {
        (value.y.atan2(value.x).to_degrees() + 360.0) % 360.0
    }

    pub fn three_point_arc(
        first: Vec3,
        through: Vec3,
        last: Vec3,
        tolerance: f64,
    ) -> Result<DraftingArc, String> {
        let a = through - first;
        let b = last - first;
        let scale = a.length().max(b.length());
        if !first.x.is_finite()
            || !first.y.is_finite()
            || !through.x.is_finite()
            || !through.y.is_finite()
            || !last.x.is_finite()
            || !last.y.is_finite()
            || scale <= tolerance
            || !scale.is_finite()
        {
            return Err("Arc points must be distinct and finite.".to_string());
        }
        let u = a * (1.0 / scale);
        let v = b * (1.0 / scale);
        let cross = Self::cross(u, v);
        if cross.abs() <= 1e-12 {
            return Err("Arc points are collinear or numerically indistinguishable.".to_string());
        }
        let uu = Vec3::dot(u, u);
        let vv = Vec3::dot(v, v);
        let offset = Vec3::xy(
            (uu * v.y - vv * u.y) / (2.0 * cross),
            (u.x * vv - v.x * uu) / (2.0 * cross),
        ) * scale;
        let center = first + offset;
        let radius = offset.length();
        if !center.x.is_finite() || !center.y.is_finite() || !radius.is_finite() || radius <= tolerance
        {
            return Err("The arc cannot be represented at this precision.".to_string());
        }
        let start = Self::degrees(first - center);
        let end = Self::degrees(last - center);
        if cross > 0.0 {
            Ok(DraftingArc::new(center, radius, start, end))
        } else {
            Ok(DraftingArc::new(center, radius, end, start))
        }
    }

    pub fn line_intersection(a: Vec3, b: Vec3, c: Vec3, d: Vec3) -> Option<(f64, f64)> {
        let mut first = b - a;
        let mut second = d - c;
        let scale = first.length().max(second.length());
        if scale <= 0.0 || !scale.is_finite() {
            return None;
        }
        first = first * (1.0 / scale);
        second = second * (1.0 / scale);
        let delta = (c - a) * (1.0 / scale);
        let determinant = Self::cross(first, second);
        if determinant.abs() <= 1e-12 * first.length() * second.length() {
            return None;
        }
        let t = Self::cross(delta, second) / determinant;
        let u = Self::cross(delta, first) / determinant;
        if t.is_finite() && u.is_finite() {
            Some((t, u))
        } else {
            None
        }
    }

    pub fn line_circle(start: Vec3, end: Vec3, center: Vec3, radius: f64) -> Vec<f64> {
        let direction = end - start;
        let length = direction.length();
        if length <= 0.0 || !length.is_finite() || radius <= 0.0 || !radius.is_finite() {
            return Vec::new();
        }
        let unit = direction * (1.0 / length);
        let delta = center - start;
        let projection = Vec3::dot(delta, unit);
        let perpendicular = Self::cross(delta, unit);
        let ratio = perpendicular / radius;
        if ratio.abs() > 1.0 + 1e-12 {
            return Vec::new();
        }
        let half = radius * (1.0 - ratio * ratio).max(0.0).sqrt();
        if half <= 1e-12 * radius {
            vec![projection / length]
        } else {
            vec![(projection - half) / length, (projection + half) / length]
        }
    }

    pub fn nearest_on_segment(point: Vec3, a: Vec3, b: Vec3) -> Vec3 {
        let delta = b - a;
        let length = delta.length();
        if length <= 0.0 || !length.is_finite() {
            return a;
        }
        let unit = delta * (1.0 / length);
        a + unit * Vec3::dot(point - a, unit).clamp(0.0, length)
    }

    pub fn segment_intersects_box(a: Vec3, b: Vec3, box2: Bounds2) -> bool {
        let mut low = 0.0_f64;
        let mut high = 1.0_f64;
        let delta = b - a;
        let mut clip = |p: f64, q: f64| -> bool {
            if p == 0.0 {
                return q >= 0.0;
            }
            let r = q / p;
            if p < 0.0 {
                if r > high {
                    return false;
                }
                low = low.max(r);
            } else {
                if r < low {
                    return false;
                }
                high = high.min(r);
            }
            true
        };
        clip(-delta.x, a.x - box2.min_x)
            && clip(delta.x, box2.max_x - a.x)
            && clip(-delta.y, a.y - box2.min_y)
            && clip(delta.y, box2.max_y - a.y)
    }

    /// Computes the intersection point of two 2D line segments `a0-a1` and `b0-b1`, if any.
    pub fn segment_intersection_2d(a0: Vec3, a1: Vec3, b0: Vec3, b1: Vec3) -> Option<Vec3> {
        let (t, u) = Self::line_intersection(a0, a1, b0, b1)?;
        if (-1e-9..=1.0 + 1e-9).contains(&t) && (-1e-9..=1.0 + 1e-9).contains(&u) {
            Some(Vec3::xy(a0.x + t * (a1.x - a0.x), a0.y + t * (a1.y - a0.y)))
        } else {
            None
        }
    }

    /// Shortest distance from point `p` to 2D segment `a-b`.
    pub fn distance_to_segment_2d(p: Vec3, a: Vec3, b: Vec3) -> f64 {
        let nearest = Self::nearest_on_segment(Vec3::xy(p.x, p.y), Vec3::xy(a.x, a.y), Vec3::xy(b.x, b.y));
        Vec3::distance(Vec3::xy(p.x, p.y), nearest)
    }

    /// Signed area of a simple 2D polygon (positive for counter-clockwise winding).
    pub fn signed_polygon_area(pts: &[Vec3]) -> f64 {
        if pts.len() < 3 {
            return 0.0;
        }
        let mut sum = 0.0;
        for i in 0..pts.len() {
            let a = pts[i];
            let b = pts[(i + 1) % pts.len()];
            sum += a.x * b.y - b.x * a.y;
        }
        0.5 * sum
    }
}

