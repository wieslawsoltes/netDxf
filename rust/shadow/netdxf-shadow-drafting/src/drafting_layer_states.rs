#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateEntryEdit {
    pub layer: String,
    pub hidden: Option<bool>,
    pub frozen: Option<bool>,
    pub locked: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SavedLayerProperties {
    pub hidden: bool,
    pub frozen: bool,
    pub locked: bool,
    pub plot: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DraftingLayerStateDetails {
    pub name: String,
    pub description: String,
    pub layers: BTreeMap<String, SavedLayerProperties>,
}

pub struct DraftingLayerStates;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateTransfer {
    pub name: String,
    pub details: DraftingLayerStateDetails,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateResourceMap {
    pub linetype_map: BTreeMap<String, String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateImportOptions {
    pub overwrite_existing: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateImportConflict {
    pub name: String,
    pub reason: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateImportAnalysis {
    pub conflicts: Vec<LayerStateImportConflict>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum LayerStateFields {
    #[default]
    All,
    VisibilityOnly,
    AppearanceOnly,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerStateEdit {
    pub name: String,
    pub entries: Vec<LayerStateEntryEdit>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLayerState {
    pub name: String,
    pub layer_count: usize,
}

impl DraftingLayerStates {
    pub fn capture(snapshot: &netdxf_shadow::ShadowSnapshot, name: impl Into<String>) -> DraftingLayerStateDetails {
        let mut layers = BTreeMap::new();
        for l in DraftingLayers::list(snapshot) {
            layers.insert(
                l.name,
                SavedLayerProperties {
                    hidden: !l.is_visible,
                    frozen: l.is_frozen,
                    locked: l.is_locked,
                    plot: true,
                },
            );
        }
        DraftingLayerStateDetails {
            name: name.into(),
            description: String::new(),
            layers,
        }
    }
}

