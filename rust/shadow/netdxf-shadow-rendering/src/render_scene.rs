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
pub struct RenderContour {
    pub points: Vec<Vec3>,
    pub closed: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderRegion {
    pub contours: Vec<RenderContour>,
    pub fill_rule: CadFillRule,
    pub argb: Option<u32>,
    pub gradient: Option<CadGradient>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderStrokeGroup {
    pub contours: Vec<RenderContour>,
    pub argb: Option<u32>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderItem {
    pub key: String,
    pub entity: ObjectId,
    pub layer: String,
    pub paint: RenderPaint,
    pub contours: Vec<RenderContour>,
    pub regions: Vec<RenderRegion>,
    pub stroke_groups: Vec<RenderStrokeGroup>,
    pub bounds: Bounds2,
    pub clip: Option<RenderClip>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderScene {
    pub document_id: String,
    pub revision: i64,
    pub layout: String,
    pub items: Vec<RenderItem>,
    pub bounds: Bounds2,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct RenderCompileOptions {
    pub curve_tolerance: f64,
    pub maximum_items: usize,
    pub background_argb: u32,
}

impl Default for RenderCompileOptions {
    fn default() -> Self {
        Self {
            curve_tolerance: 0.05,
            maximum_items: 1_000_000,
            background_argb: 0xFF18_1A1F,
        }
    }
}

