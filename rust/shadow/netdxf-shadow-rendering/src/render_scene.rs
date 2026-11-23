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

impl RenderContour {
    pub fn pattern_breaks(&self) -> &[usize] {
        &[]
    }
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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RenderItem {
    pub key: String,
    pub entity: ObjectId,
    pub layer: String,
    pub insert_path: Vec<ObjectId>,
    pub paint: RenderPaint,
    pub contours: Vec<RenderContour>,
    pub strokes: Vec<RenderContour>,
    pub regions: Vec<RenderRegion>,
    pub stroke_groups: Vec<RenderStrokeGroup>,
    pub filled: bool,
    pub is_presentation_only: bool,
    pub bounds: Bounds2,
    pub clip: Option<RenderClip>,
    pub clips: Vec<RenderClip>,
    pub image: Option<String>,
    pub image_clip: Option<RenderClip>,
    pub style: Option<String>,
    pub text: Option<String>,
    pub text_clip: Option<RenderClip>,
    pub text_layout: Option<RichTextLayoutResult>,
    pub source_to_world: Affine3,
    pub pattern_to_world: Affine3,
    pub projection: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RenderScene {
    pub document_id: String,
    pub revision: i64,
    pub layout: String,
    pub items: Vec<RenderItem>,
    pub diagnostics: Vec<String>,
    pub bounds: Bounds2,
}

impl RenderScene {
    pub fn index(&self) -> usize {
        self.items.len()
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderCompileOptions {
    pub curve_tolerance: f64,
    pub tolerance: f64,
    pub maximum_items: usize,
    pub background_argb: u32,
    pub layout: Option<String>,
    pub root_block: Option<String>,
    pub root_entities: Vec<ObjectId>,
    pub include_paper_viewports: bool,
    pub paper_space: bool,
    pub linetype_scale: f64,
    pub linetype_alignment: bool,
    pub model_annotation: Option<ModelAnnotationContext>,
    pub viewport_overrides: BTreeMap<String, ViewportLayerOverride>,
}

impl Default for RenderCompileOptions {
    fn default() -> Self {
        Self {
            curve_tolerance: 0.05,
            tolerance: 0.05,
            maximum_items: 1_000_000,
            background_argb: 0xFF18_1A1F,
            layout: None,
            root_block: None,
            root_entities: Vec::new(),
            include_paper_viewports: true,
            paper_space: false,
            linetype_scale: 1.0,
            linetype_alignment: true,
            model_annotation: None,
            viewport_overrides: BTreeMap::new(),
        }
    }
}
