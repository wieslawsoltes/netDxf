#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ResourceEdit {
    pub name: String,
    pub description: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypeSegmentEdit {
    pub length: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LinetypeEdit {
    pub name: String,
    pub description: String,
    pub segments: Vec<LinetypeSegmentEdit>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextStyleEdit {
    pub name: String,
    pub font_file: String,
    pub height: f64,
    pub width_factor: f64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DraftingResourceStatus {
    pub name: String,
    pub in_use: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingLinetype {
    pub name: String,
    pub description: String,
    pub length: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingTextStyle {
    pub name: String,
    pub font_file: String,
    pub height: f64,
}

pub struct DraftingResources;

impl DraftingResources {
    pub fn linetype_statuses(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingResourceStatus> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Linetype")
            .map(|o| DraftingResourceStatus {
                name: snapshot.read(o.id.clone()).field("Name").text().to_string(),
                in_use: true,
            })
            .collect()
    }
}

