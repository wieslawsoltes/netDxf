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
pub struct StrokePattern {
    pub dashes: Vec<f64>,
    pub phase: f64,
}

impl StrokePattern {
    pub fn apply(&self, contour: &RenderContour) -> Vec<RenderContour> {
        vec![contour.clone()]
    }

    pub fn get_contours(&self, contours: &[RenderContour]) -> Vec<RenderContour> {
        contours.to_vec()
    }
}

