#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingAttributeDefinition {
    pub id: ObjectId,
    pub tag: String,
    pub prompt: String,
    pub default_value: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AttributeDefinitionEdit {
    pub id: ObjectId,
    pub tag: Option<String>,
    pub prompt: Option<String>,
    pub default_value: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AttributeDefinitionCreation {
    pub tag: String,
    pub prompt: String,
    pub default_value: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AttributeDefinitionChanges {
    pub additions: Vec<AttributeDefinitionCreation>,
    pub edits: Vec<AttributeDefinitionEdit>,
    pub removals: Vec<ObjectId>,
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
                }
            })
            .collect()
    }
}

