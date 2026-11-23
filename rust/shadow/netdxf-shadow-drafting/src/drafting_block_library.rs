#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockLibraryConflict {
    #[default]
    Skip,
    Reject,
    Overwrite,
    Rename,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockLibraryResourceConflict {
    #[default]
    KeepTarget,
    Reuse,
    UseSource,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct BlockLibraryImport {
    pub name: String,
    pub source_block: String,
    pub block_names: Vec<String>,
    pub conflict_policy: BlockLibraryConflict,
    pub block_conflict: BlockLibraryConflict,
    pub resource_policy: BlockLibraryResourceConflict,
    pub resource_conflict: BlockLibraryResourceConflict,
    pub preserve_layer_zero: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryLimits {
    pub maximum_blocks: usize,
    pub maximum_entities: usize,
    pub maximum_objects: usize,
    pub maximum_value_depth: usize,
    pub maximum_values: usize,
}

impl Default for BlockLibraryLimits {
    fn default() -> Self {
        Self {
            maximum_blocks: 4_096,
            maximum_entities: 200_000,
            maximum_objects: 200_000,
            maximum_value_depth: 64,
            maximum_values: 1_000_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct BlockLibraryResource {
    pub kind: String,
    pub table: String,
    pub name: String,
    pub action: String,
    pub source: Option<ObjectId>,
    pub source_name: String,
    pub target: Option<ObjectId>,
    pub target_name: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct BlockLibraryPlan {
    pub block: Option<ObjectId>,
    pub edit: Option<String>,
    pub identities: Vec<ObjectId>,
    pub imported_blocks: Vec<String>,
    pub resources: Vec<BlockLibraryResource>,
}

pub struct DraftingBlockLibrary;

impl DraftingBlockLibrary {
    pub fn import(request: &BlockLibraryImport, limits: &BlockLibraryLimits) -> BlockLibraryPlan {
        Self::plan_import(request, limits)
    }

    pub fn plan_import(request: &BlockLibraryImport, limits: &BlockLibraryLimits) -> BlockLibraryPlan {
        let imported_blocks = request
            .block_names
            .iter()
            .take(limits.maximum_blocks)
            .cloned()
            .collect();
        BlockLibraryPlan {
            imported_blocks,
            resources: Vec::new(),
            ..Default::default()
        }
    }
}

