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

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypePlanOptions {
    pub metric_space: LinetypeMetricSpace,
    pub rotation_pivot: LinetypeTextRotationPivot,
    pub text_rotation_pivot: LinetypeTextRotationPivot,
    pub text_pivot: LinetypeTextRotationPivot,
    pub frame_mode: LinetypeFrameMode,
    pub close_frame_seam: bool,
    pub maximum_elements: usize,
    pub maximum_points: usize,
    pub maximum_placements: usize,
    pub maximum_scene_placements: usize,
    pub maximum_expansion_work: usize,
    pub maximum_scene_expansion_work: usize,
    pub maximum_text_length: usize,
}

impl Default for LinetypePlanOptions {
    fn default() -> Self {
        Self {
            metric_space: LinetypeMetricSpace::default(),
            rotation_pivot: LinetypeTextRotationPivot::default(),
            text_rotation_pivot: LinetypeTextRotationPivot::default(),
            text_pivot: LinetypeTextRotationPivot::default(),
            frame_mode: LinetypeFrameMode::default(),
            close_frame_seam: true,
            maximum_elements: 64,
            maximum_points: 100_000,
            maximum_placements: 10_000,
            maximum_scene_placements: 100_000,
            maximum_expansion_work: 50_000,
            maximum_scene_expansion_work: 500_000,
            maximum_text_length: 256,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypePlacement {
    pub distance: f64,
    pub position: Vec3,
    pub tangent: Vec3,
}

pub struct LinetypePlanner;

impl LinetypePlanner {
    /// Walks a polyline by arc length and emits placements every `spacing` units.
    pub fn plan(points: &[Vec3], spacing: f64) -> Vec<LinetypePlacement> {
        if points.len() < 2 || spacing <= 1e-9 {
            return Vec::new();
        }
        let mut placements = Vec::new();
        let mut accum = 0.0;
        let mut next_target = spacing;
        for w in points.windows(2) {
            let seg = w[1] - w[0];
            let len = seg.length();
            if len <= 1e-12 {
                continue;
            }
            let tangent = seg / len;
            while next_target <= accum + len && placements.len() < 100_000 {
                let local = next_target - accum;
                placements.push(LinetypePlacement {
                    distance: next_target,
                    position: w[0] + tangent * local,
                    tangent,
                });
                next_target += spacing;
            }
            accum += len;
        }
        placements
    }
}

