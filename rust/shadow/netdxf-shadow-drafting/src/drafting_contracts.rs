#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingCommand {
    pub name: String,
    pub summary: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DraftingTransformMeasure {
    pub translation: Vec3,
    pub rotation_radians: f64,
    pub scale: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingTransformOptions {
    pub copy: bool,
    pub uniform_scale: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLimits {
    pub maximum_entities: usize,
    pub maximum_vertices: usize,
}

impl Default for DraftingLimits {
    fn default() -> Self {
        Self {
            maximum_entities: 100_000,
            maximum_vertices: 1_000_000,
        }
    }
}

#[derive(Debug, Clone)]
pub struct DraftingPlan {
    pub command: DraftingCommand,
    pub patch: ShadowGraphPatch,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingGrip {
    pub entity: ObjectId,
    pub key: String,
    pub position: Vec3,
}

pub struct DraftingGuard;

impl DraftingGuard {
    pub fn validate_limits(entity_count: usize, limits: &DraftingLimits) -> Result<(), String> {
        if entity_count > limits.maximum_entities {
            Err("Entity count exceeds drafting limits.".into())
        } else {
            Ok(())
        }
    }
}

