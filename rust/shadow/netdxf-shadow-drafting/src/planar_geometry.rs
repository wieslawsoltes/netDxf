#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
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
}

pub struct PlanarGeometry;

impl PlanarGeometry {
    /// Computes the intersection point of two 2D line segments `a0-a1` and `b0-b1`, if any.
    pub fn segment_intersection_2d(a0: Vec3, a1: Vec3, b0: Vec3, b1: Vec3) -> Option<Vec3> {
        let r = Vec3::xy(a1.x - a0.x, a1.y - a0.y);
        let s = Vec3::xy(b1.x - b0.x, b1.y - b0.y);
        let denom = r.x * s.y - r.y * s.x;
        if denom.abs() < 1e-12 {
            return None;
        }
        let qp = Vec3::xy(b0.x - a0.x, b0.y - a0.y);
        let t = (qp.x * s.y - qp.y * s.x) / denom;
        let u = (qp.x * r.y - qp.y * r.x) / denom;
        if (-1e-9..=1.0 + 1e-9).contains(&t) && (-1e-9..=1.0 + 1e-9).contains(&u) {
            Some(Vec3::xy(a0.x + t * r.x, a0.y + t * r.y))
        } else {
            None
        }
    }

    /// Shortest distance from point `p` to 2D segment `a-b`.
    pub fn distance_to_segment_2d(p: Vec3, a: Vec3, b: Vec3) -> f64 {
        let ab = Vec3::xy(b.x - a.x, b.y - a.y);
        let len_sq = ab.x * ab.x + ab.y * ab.y;
        if len_sq <= 1e-18 {
            return Vec3::distance(Vec3::xy(p.x, p.y), Vec3::xy(a.x, a.y));
        }
        let t = (((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len_sq).clamp(0.0, 1.0);
        let proj = Vec3::xy(a.x + t * ab.x, a.y + t * ab.y);
        Vec3::distance(Vec3::xy(p.x, p.y), proj)
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

