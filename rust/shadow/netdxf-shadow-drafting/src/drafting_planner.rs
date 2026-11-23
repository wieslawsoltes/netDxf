#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BlockCreation {
    pub name: String,
    pub description: String,
    pub origin: Vec3,
    pub base_point: Vec3,
    pub layer: String,
    pub disposition: BlockSourceDisposition,
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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BlockDefinitionRequest {
    pub name: String,
    pub description: String,
    pub origin: Vec3,
    pub base_point: Vec3,
    pub insert_layer: String,
    pub redefine: bool,
    pub entities: Vec<ObjectId>,
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

    pub fn definition(snapshot: &netdxf_shadow::ShadowSnapshot, name: &str) -> Option<ObjectId> {
        snapshot
            .objects
            .values()
            .find(|o| {
                o.short_type() == "Block"
                    && snapshot
                        .read(o.id.clone())
                        .field("Name")
                        .text()
                        .eq_ignore_ascii_case(name)
            })
            .map(|o| o.id.clone())
    }

    pub fn validate_name(name: &str) -> Result<(), String> {
        if name.trim().is_empty() {
            Err("Block name cannot be empty.".into())
        } else {
            Ok(())
        }
    }

    pub fn validate_reference(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        name: &str,
    ) -> Result<ObjectId, String> {
        Self::definition(snapshot, name).ok_or_else(|| format!("Unknown block '{name}'."))
    }

    pub fn rename(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        block_id: &ObjectId,
        new_name: impl Into<String>,
    ) -> Result<DraftingPlan, String> {
        DraftingPlanner::rename_block(snapshot, block_id, new_name)
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

    pub fn plan(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        command: &DraftingCommand,
    ) -> DraftingPlan {
        Self::empty_plan(snapshot, command.name.clone())
    }

    pub fn plan_in_block(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        _block_name: &str,
        command: &DraftingCommand,
    ) -> DraftingPlan {
        Self::plan(snapshot, command)
    }

    pub fn create_block(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        request: &BlockCreation,
    ) -> DraftingPlan {
        let id = ObjectId::new(format!("O:Block:{}", request.name));
        let mut obj = netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Blocks.Block");
        obj.properties.insert(
            "Name".into(),
            netdxf_shadow::ShadowValue::from_string(request.name.clone()),
        );
        obj.properties.insert(
            "Origin".into(),
            netdxf_shadow::ShadowValue::from_vec3(request.origin, false),
        );
        let refs = request
            .entities
            .iter()
            .cloned()
            .map(netdxf_shadow::ShadowValue::reference)
            .collect();
        obj.properties
            .insert("Entities".into(), netdxf_shadow::ShadowValue::sequence(refs));
        DraftingPlan {
            command: DraftingCommand {
                name: "CREATE BLOCK".into(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                vec![netdxf_shadow::ShadowObjectChange {
                    id,
                    before: None,
                    after: Some(obj),
                }],
            ),
        }
    }

    pub fn create_block_in_definition(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        _owner_block: &str,
        request: &BlockCreation,
    ) -> DraftingPlan {
        Self::create_block(snapshot, request)
    }

    pub fn plan_block_definition(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        request: &BlockDefinitionRequest,
    ) -> DraftingPlan {
        Self::create_block(
            snapshot,
            &BlockCreation {
                name: request.name.clone(),
                origin: request.origin,
                entities: Vec::new(),
                ..Default::default()
            },
        )
    }

    pub fn plan_block_definition_in_definition(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        owner_block: &str,
        request: &BlockDefinitionRequest,
    ) -> DraftingPlan {
        Self::create_block_in_definition(
            snapshot,
            owner_block,
            &BlockCreation {
                name: request.name.clone(),
                origin: request.origin,
                entities: Vec::new(),
                ..Default::default()
            },
        )
    }

    pub fn rename_block(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        block_id: &ObjectId,
        new_name: impl Into<String>,
    ) -> Result<DraftingPlan, String> {
        let existing = snapshot
            .objects
            .get(block_id)
            .ok_or_else(|| "Unknown block.".to_string())?;
        let mut updated = (**existing).clone();
        updated.properties.insert(
            "Name".into(),
            netdxf_shadow::ShadowValue::from_string(new_name.into()),
        );
        Ok(DraftingPlan {
            command: DraftingCommand {
                name: "RENAME BLOCK".into(),
                ..Default::default()
            },
            patch: ShadowGraphPatch::new(
                snapshot.document_id.clone(),
                snapshot.revision,
                vec![netdxf_shadow::ShadowObjectChange {
                    id: block_id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                }],
            ),
        })
    }
}

