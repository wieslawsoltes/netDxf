#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateEntryEdit {
    pub name: String,
    pub layer: String,
    pub visible: Option<bool>,
    pub hidden: Option<bool>,
    pub frozen: Option<bool>,
    pub locked: Option<bool>,
    pub frozen_in_new_viewports: Option<bool>,
    pub plot: Option<bool>,
    pub color: Option<String>,
    pub color_index: Option<i16>,
    pub linetype: Option<String>,
    pub lineweight: Option<i16>,
    pub transparency: Option<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SavedLayerProperties {
    pub name: String,
    pub flags: i32,
    pub hidden: bool,
    pub frozen: bool,
    pub locked: bool,
    pub plot: bool,
    pub color: Option<String>,
    pub color_index: i16,
    pub true_color: Option<u32>,
    pub linetype: String,
    pub lineweight: i16,
    pub transparency: u8,
    pub stored_alpha_value: Option<i32>,
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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerStateEdit {
    pub id: Option<ObjectId>,
    pub action: String,
    pub name: String,
    pub description: Option<String>,
    pub current_layer: Option<String>,
    pub fields: LayerStateFields,
    pub restore_current_layer: bool,
    pub turn_off_unlisted_layers: bool,
    pub add_layers: Vec<LayerStateEntryEdit>,
    pub remove_layers: Vec<String>,
    pub entries: Vec<LayerStateEntryEdit>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLayerState {
    pub name: String,
    pub layer_count: usize,
}

impl DraftingLayerStates {
    pub fn capture(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        name: impl Into<String>,
    ) -> DraftingLayerStateDetails {
        let mut layers = BTreeMap::new();
        for l in DraftingLayers::list(snapshot) {
            layers.insert(
                l.name.clone(),
                SavedLayerProperties {
                    name: l.name,
                    flags: 0,
                    hidden: !l.is_visible,
                    frozen: l.is_frozen,
                    locked: l.is_locked,
                    plot: l.plot,
                    color: l.color,
                    color_index: l.color_index,
                    true_color: l.true_color,
                    linetype: l.linetype,
                    lineweight: l.lineweight,
                    transparency: l.transparency,
                    stored_alpha_value: None,
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
