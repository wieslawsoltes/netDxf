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

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderPaint {
    pub argb: u32,
    pub lineweight: f64,
    pub pattern: Option<StrokePattern>,
    pub gradient: Option<GradientPlan>,
}

impl Default for RenderPaint {
    fn default() -> Self {
        Self {
            argb: 0xFFFF_FFFF,
            lineweight: 0.25,
            pattern: None,
            gradient: None,
        }
    }
}

