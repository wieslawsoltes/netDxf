#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::f64::consts::TAU;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadLinetypeAlignment {
    #[default]
    Continuous,
    PerSegment,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadStyle {
    pub argb: u32,
    pub lineweight: f64,
    pub dashes: Vec<f64>,
    pub visible: bool,
    pub layer: String,
    pub linetype: Option<CadLinetype>,
    pub alignment: CadLinetypeAlignment,
}

impl Default for CadStyle {
    fn default() -> Self {
        Self {
            argb: 0xFFFF_FFFF,
            lineweight: 0.25,
            dashes: Vec::new(),
            visible: true,
            layer: "0".into(),
            linetype: None,
            alignment: CadLinetypeAlignment::Continuous,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadContour {
    pub curves: Vec<Curve3>,
    pub closed: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct CadLineSpacing {
    pub factor: f64,
    pub exact: bool,
}

impl Default for CadLineSpacing {
    fn default() -> Self {
        Self {
            factor: 1.0,
            exact: false,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadText {
    pub content: String,
    pub origin: Vec3,
    pub x_axis: Vec3,
    pub y_axis: Vec3,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub font: String,
    pub is_mtext: bool,
    pub line_spacing: CadLineSpacing,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadImage {
    pub file_name: String,
    pub corners: Vec<Vec3>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadGeometry {
    pub contours: Vec<CadContour>,
    pub filled: bool,
    pub regions: Vec<CadRegion>,
    pub stroke_groups: Vec<CadStrokeGroup>,
    pub texts: Vec<CadText>,
    pub images: Vec<CadImage>,
    pub points: Vec<Vec3>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadOccurrence {
    pub key: String,
    pub entity: ObjectId,
    pub insert_path: Vec<ObjectId>,
    pub transform: Affine3,
    pub style: CadStyle,
    pub geometry: CadGeometry,
    pub bounds: Bounds2,
    pub components: Vec<CadComponent>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadScene {
    pub document_id: String,
    pub revision: i64,
    pub layout: String,
    pub occurrences: Vec<CadOccurrence>,
    pub bounds: Bounds2,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SceneOptions {
    pub layout: String,
    pub maximum_depth: usize,
    pub maximum_occurrences: usize,
    pub include_invisible: bool,
}

impl Default for SceneOptions {
    fn default() -> Self {
        Self {
            layout: "Model".into(),
            maximum_depth: 64,
            maximum_occurrences: 1_000_000,
            include_invisible: false,
        }
    }
}

/// Projects a single shadow entity into [`CadGeometry`].
pub trait IEntityProjector: Send + Sync {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry>;
}

