#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerIsolationEdit {
    pub keep_layers: Vec<String>,
    pub lock_others: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerIsolationStatus {
    pub isolated: bool,
    pub active_layers: Vec<String>,
}

pub struct DraftingLayerIsolation;

impl DraftingLayerIsolation {
    pub fn isolate(edit: &LayerIsolationEdit) -> LayerIsolationStatus {
        LayerIsolationStatus {
            isolated: !edit.keep_layers.is_empty(),
            active_layers: edit.keep_layers.clone(),
        }
    }
}

