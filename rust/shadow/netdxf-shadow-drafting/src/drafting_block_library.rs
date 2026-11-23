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
    Overwrite,
    Rename,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum BlockLibraryResourceConflict {
    #[default]
    KeepTarget,
    UseSource,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryImport {
    pub block_names: Vec<String>,
    pub conflict_policy: BlockLibraryConflict,
    pub resource_policy: BlockLibraryResourceConflict,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryLimits {
    pub maximum_blocks: usize,
    pub maximum_entities: usize,
}

impl Default for BlockLibraryLimits {
    fn default() -> Self {
        Self {
            maximum_blocks: 4_096,
            maximum_entities: 200_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BlockLibraryResource {
    pub kind: String,
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct BlockLibraryPlan {
    pub imported_blocks: Vec<String>,
    pub resources: Vec<BlockLibraryResource>,
}

pub struct DraftingBlockLibrary;

impl DraftingBlockLibrary {
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
        }
    }
}

