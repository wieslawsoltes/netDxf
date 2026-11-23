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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct GradientPlan {
    pub kind: String,
    pub color1: u32,
    pub color2: u32,
    pub colors: Vec<u32>,
    pub positions: Vec<f64>,
    pub start: Vec3,
    pub end: Vec3,
    pub shift: f64,
    pub radial: bool,
    pub radius: f64,
}

impl GradientPlan {
    pub fn create(start: Vec3, end: Vec3, color1: u32, color2: u32) -> Self {
        Self {
            kind: "Linear".into(),
            color1,
            color2,
            colors: vec![color1, color2],
            positions: vec![0.0, 1.0],
            start,
            end,
            shift: 0.0,
            radial: false,
            radius: Vec3::distance(start, end),
        }
    }
}

