#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingCommand {
    pub name: String,
    pub summary: String,
    pub entities: Vec<ObjectId>,
    pub points: Vec<Vec3>,
    pub base_point: Option<Vec3>,
    pub reference: Option<Vec3>,
    pub secondary: Option<Vec3>,
    pub layer: Option<String>,
    pub closed: bool,
    pub count: Option<usize>,
    pub rotation: Option<f64>,
    pub scale: Option<f64>,
    pub width: Option<f64>,
    pub text: Option<String>,
    pub value: Option<f64>,
    pub transform: Option<DraftingTransformOptions>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DraftingTransformMeasure {
    pub translation: Vec3,
    pub rotation_radians: f64,
    pub scale: f64,
}

impl DraftingTransformMeasure {
    pub fn points(&self) -> [Vec3; 1] {
        [self.translation]
    }

    pub fn value(&self) -> f64 {
        self.scale
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingTransformOptions {
    pub copy: bool,
    pub uniform_scale: bool,
    pub reference: Option<Vec3>,
    pub target: Option<Vec3>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct DraftingLimits {
    pub maximum_entities: usize,
    pub maximum_vertices: usize,
    pub maximum_cloned_objects: usize,
    pub maximum_coordinate: f64,
    pub maximum_points: usize,
    pub minimum_length: f64,
}

impl Default for DraftingLimits {
    fn default() -> Self {
        Self {
            maximum_entities: 100_000,
            maximum_vertices: 1_000_000,
            maximum_cloned_objects: 25_000,
            maximum_coordinate: 1e12,
            maximum_points: 100_000,
            minimum_length: 1e-9,
        }
    }
}

#[derive(Debug, Clone)]
pub struct DraftingPlan {
    pub command: DraftingCommand,
    pub patch: ShadowGraphPatch,
}

impl DraftingPlan {
    #[inline]
    pub fn description(&self) -> &str {
        &self.command.name
    }

    #[inline]
    pub fn created(&self) -> Vec<ObjectId> {
        self.patch
            .changes
            .iter()
            .filter(|c| c.before.is_none() && c.after.is_some())
            .map(|c| c.id.clone())
            .collect()
    }

    pub fn execute(
        &self,
        snapshot: &netdxf_shadow::ShadowSnapshot,
    ) -> netdxf_shadow::ShadowResult<netdxf_shadow::ShadowSnapshot> {
        self.patch.apply(snapshot)
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingGrip {
    pub id: String,
    pub entity: ObjectId,
    pub key: String,
    pub property: String,
    pub kind: String,
    pub index: usize,
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

    pub fn current(snapshot: &netdxf_shadow::ShadowSnapshot, expected_revision: i64) -> Result<(), String> {
        if snapshot.revision != expected_revision {
            Err("The document changed. Refresh before editing.".into())
        } else {
            Ok(())
        }
    }

    pub fn entity(snapshot: &netdxf_shadow::ShadowSnapshot, id: &ObjectId) -> Result<(), String> {
        if snapshot.objects.contains_key(id) {
            Ok(())
        } else {
            Err(format!("Unknown entity '{}'.", id.as_str()))
        }
    }

    pub fn planar(pt: Vec3) -> Result<Vec3, String> {
        if pt.x.is_finite() && pt.y.is_finite() && pt.z.is_finite() {
            Ok(pt)
        } else {
            Err("Coordinates must be finite.".into())
        }
    }

    pub fn selection(ids: &[ObjectId], max_len: usize) -> Result<(), String> {
        if ids.len() > max_len {
            Err(format!("Selection exceeds limit of {max_len} entities."))
        } else {
            Ok(())
        }
    }
}
