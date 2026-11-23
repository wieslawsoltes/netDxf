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
pub struct RenderClip {
    pub bounds: Bounds2,
    pub polygon: Vec<Vec3>,
}

impl RenderClip {
    pub fn contours(&self) -> Vec<RenderContour> {
        if self.polygon.is_empty() {
            Vec::new()
        } else {
            vec![RenderContour {
                points: self.polygon.clone(),
                closed: true,
            }]
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PaperViewportProjection {
    pub viewport_entity: Option<ObjectId>,
    pub transform: Affine3,
    pub model_to_paper: Affine3,
    pub paper_to_model: Affine3,
    pub scale: f64,
    pub perspective: Option<PerspectiveCamera>,
    pub clip: RenderClip,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct PaperSpaceOptions {
    pub include_viewport_borders: bool,
    pub respect_layer_overrides: bool,
    pub perspective: bool,
    pub maximum_viewports: usize,
    pub maximum_items: usize,
    pub maximum_candidates: usize,
    pub maximum_clip_points: usize,
    pub maximum_pattern_scales: usize,
}

