#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerIsolationEdit {
    pub action: String,
    pub mode: String,
    pub layers: Vec<String>,
    pub keep_layers: Vec<String>,
    pub lock_others: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerIsolationStatus {
    pub isolated: bool,
    pub active: bool,
    pub valid: bool,
    pub layer_count: usize,
    pub mode: String,
    pub notice: Option<String>,
    pub active_layers: Vec<String>,
}

pub struct DraftingLayerIsolation;

impl DraftingLayerIsolation {
    pub const APPLICATION_NAME: &'static str = "NETDXF_SHADOW_LAYER_ISOLATION";

    pub fn isolate(edit: &LayerIsolationEdit) -> LayerIsolationStatus {
        let active = !edit.keep_layers.is_empty() || !edit.layers.is_empty();
        LayerIsolationStatus {
            isolated: active,
            active,
            valid: true,
            layer_count: edit.keep_layers.len().max(edit.layers.len()),
            mode: if edit.lock_others { "lock" } else { "hide" }.into(),
            notice: None,
            active_layers: if edit.keep_layers.is_empty() {
                edit.layers.clone()
            } else {
                edit.keep_layers.clone()
            },
        }
    }

    pub fn read(_snapshot: &netdxf_shadow::ShadowSnapshot) -> LayerIsolationStatus {
        LayerIsolationStatus::default()
    }

    pub fn plan(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        _edit: &LayerIsolationEdit,
    ) -> DraftingPlan {
        DraftingPlanner::empty_plan(snapshot, "ISOLATE LAYERS")
    }
}

