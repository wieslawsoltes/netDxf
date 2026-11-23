#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadFillRule, CadGradient, CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum LinetypeFrameMode {
    #[default]
    Planar,
    AuthoredPlane,
    ParallelTransport,
}

pub struct LinetypeTransport;

impl LinetypeTransport {
    pub fn create(points: &[Vec3], normal_hint: Vec3) -> Vec<(Vec3, Vec3, Vec3)> {
        Self::compute_frames(points, normal_hint)
    }

    pub fn at(frames: &[(Vec3, Vec3, Vec3)], index: usize) -> Option<(Vec3, Vec3, Vec3)> {
        frames.get(index).copied()
    }

    /// Computes orthonormal tangent/normal/binormal frames along a polyline.
    pub fn compute_frames(points: &[Vec3], normal_hint: Vec3) -> Vec<(Vec3, Vec3, Vec3)> {
        if points.len() < 2 {
            return Vec::new();
        }
        let n = normal_hint.normalized();
        let mut frames = Vec::with_capacity(points.len());
        for i in 0..points.len() {
            let t = if i + 1 < points.len() {
                (points[i + 1] - points[i]).normalized()
            } else {
                (points[i] - points[i - 1]).normalized()
            };
            let b = Vec3::cross(n, t).normalized();
            frames.push((t, b, n));
        }
        frames
    }
}

