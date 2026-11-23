#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct DraftingAttributeDefinition {
    pub id: ObjectId,
    pub tag: String,
    pub prompt: String,
    pub default_value: String,
    pub position: Vec3,
    pub appearance: Option<String>,
    pub preset: bool,
    pub verify: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AttributeDefinitionEdit {
    pub id: ObjectId,
    pub definition: Option<ObjectId>,
    pub tag: Option<String>,
    pub prompt: Option<String>,
    pub default_value: Option<String>,
    pub appearance: Option<String>,
    pub preset: Option<bool>,
    pub verify: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AttributeDefinitionCreation {
    pub tag: String,
    pub prompt: String,
    pub value: String,
    pub default_value: String,
    pub position: Vec3,
    pub layer: String,
    pub text_style: String,
    pub height: f64,
    pub invisible: bool,
    pub constant: bool,
    pub verify: bool,
    pub preset: bool,
    pub multiline: bool,
    pub position_locked: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AttributeDefinitionChanges {
    pub additions: Vec<AttributeDefinitionCreation>,
    pub add: Vec<AttributeDefinitionCreation>,
    pub edits: Vec<AttributeDefinitionEdit>,
    pub removals: Vec<ObjectId>,
    pub remove: Vec<ObjectId>,
    pub order: Vec<ObjectId>,
}

pub struct DraftingAttributeDefinitions;

impl DraftingAttributeDefinitions {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingAttributeDefinition> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "AttributeDefinition")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingAttributeDefinition {
                    id: o.id.clone(),
                    tag: r.field("Tag").text().to_string(),
                    prompt: r.field("Prompt").text().to_string(),
                    default_value: r.field("Value").text().to_string(),
                    position: r.field("Position").vector(Vec3::ZERO),
                    appearance: None,
                    preset: r.field("Preset").boolean(false),
                    verify: r.field("Verify").boolean(false),
                }
            })
            .collect()
    }

    pub fn read(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingAttributeDefinition> {
        Self::list(snapshot)
    }

    pub fn owner(_snapshot: &netdxf_shadow::ShadowSnapshot, block_name: &str) -> ObjectId {
        ObjectId::new(format!("O:Block:{block_name}"))
    }

    pub fn plan(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        _changes: &AttributeDefinitionChanges,
    ) -> DraftingPlan {
        DraftingPlanner::empty_plan(snapshot, "EDIT ATTRIBUTE DEFINITIONS")
    }
}

