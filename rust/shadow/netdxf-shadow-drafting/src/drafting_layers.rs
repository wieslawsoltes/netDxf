#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingLayer {
    pub id: ObjectId,
    pub name: String,
    pub is_visible: bool,
    pub is_frozen: bool,
    pub is_locked: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct LayerEdit {
    pub name: String,
    pub is_visible: Option<bool>,
    pub is_frozen: Option<bool>,
    pub is_locked: Option<bool>,
}

pub struct DraftingLayers;

impl DraftingLayers {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingLayer> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Layer")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingLayer {
                    id: o.id.clone(),
                    name: r.field("Name").text().to_string(),
                    is_visible: r.field("IsVisible").boolean(true),
                    is_frozen: r.field("IsFrozen").boolean(false),
                    is_locked: r.field("IsLocked").boolean(false),
                }
            })
            .collect()
    }
}

