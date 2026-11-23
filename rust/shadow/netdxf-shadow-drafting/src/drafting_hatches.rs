#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingHatchLine {
    pub angle: f64,
    pub origin: Vec3,
    pub delta: Vec3,
    pub dashes: Vec<f64>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DraftingHatchPatternType {
    #[default]
    Solid,
    Predefined,
    UserDefined,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingHatchPattern {
    pub name: String,
    pub pattern_type: DraftingHatchPatternType,
    pub scale: f64,
    pub angle: f64,
    pub lines: Vec<DraftingHatchLine>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct HatchAuthoringRequest {
    pub layer: String,
    pub pattern: DraftingHatchPattern,
    pub boundary: Vec<Vec3>,
}

pub struct DraftingHatches;

impl DraftingHatches {
    pub fn solid_pattern() -> DraftingHatchPattern {
        DraftingHatchPattern {
            name: "SOLID".into(),
            pattern_type: DraftingHatchPatternType::Solid,
            scale: 1.0,
            angle: 0.0,
            lines: Vec::new(),
        }
    }
}

