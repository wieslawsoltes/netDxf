#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BlockCreation {
    pub name: String,
    pub origin: Vec3,
    pub entities: Vec<ObjectId>,
}

pub struct DraftingBlocks;

pub struct DraftingPlanner;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockSourceDisposition {
    #[default]
    Retain,
    ConvertToInsert,
    Delete,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BlockDefinitionRequest {
    pub name: String,
    pub origin: Vec3,
    pub disposition: BlockSourceDisposition,
}

impl DraftingBlocks {
    pub fn list_names(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<String> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Block")
            .map(|o| snapshot.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }
}

impl DraftingPlanner {
    pub fn empty_plan(snapshot: &netdxf_shadow::ShadowSnapshot, name: impl Into<String>) -> DraftingPlan {
        DraftingPlan {
            command: DraftingCommand {
                name: name.into(),
                summary: String::new(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(snapshot.document_id.clone(), snapshot.revision, Vec::new()),
        }
    }
}

