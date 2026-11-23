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
    pub description: String,
    pub is_visible: bool,
    pub is_frozen: bool,
    pub is_locked: bool,
    pub frozen_in_new_viewports: bool,
    pub plot: bool,
    pub color: Option<String>,
    pub color_index: i16,
    pub true_color: Option<u32>,
    pub linetype: String,
    pub lineweight: i16,
    pub transparency: u8,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct LayerEdit {
    pub id: Option<ObjectId>,
    pub name: String,
    pub description: Option<String>,
    pub is_visible: Option<bool>,
    pub is_frozen: Option<bool>,
    pub is_locked: Option<bool>,
    pub frozen_in_new_viewports: Option<bool>,
    pub plot: Option<bool>,
    pub color: Option<String>,
    pub color_index: Option<i16>,
    pub linetype: Option<String>,
    pub lineweight: Option<i16>,
    pub transparency: Option<u8>,
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
                    description: r.field("Description").text().to_string(),
                    is_visible: r.field("IsVisible").boolean(true),
                    is_frozen: r.field("IsFrozen").boolean(false),
                    is_locked: r.field("IsLocked").boolean(false),
                    frozen_in_new_viewports: r.field("FrozenInNewViewports").boolean(false),
                    plot: r.field("Plot").boolean(true),
                    color: None,
                    color_index: r.field("ColorIndex").number(7.0) as i16,
                    true_color: None,
                    linetype: r.field("Linetype").text().to_string(),
                    lineweight: r.field("Lineweight").number(-3.0) as i16,
                    transparency: r.field("Transparency").number(0.0) as u8,
                }
            })
            .collect()
    }
}
