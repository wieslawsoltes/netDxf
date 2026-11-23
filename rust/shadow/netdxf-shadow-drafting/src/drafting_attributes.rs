#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct DraftingAttribute {
    pub id: ObjectId,
    pub tag: String,
    pub value: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AttributeEdit {
    pub id: ObjectId,
    pub value: String,
}

pub struct DraftingAttributes;

impl DraftingAttributes {
    pub fn list(snapshot: &netdxf_shadow::ShadowSnapshot) -> Vec<DraftingAttribute> {
        snapshot
            .objects
            .values()
            .filter(|o| o.short_type() == "Attribute")
            .map(|o| {
                let r = snapshot.read(o.id.clone());
                DraftingAttribute {
                    id: o.id.clone(),
                    tag: r.field("Tag").text().to_string(),
                    value: r.field("Value").text().to_string(),
                    position: r.field("Position").vector(Vec3::ZERO),
                }
            })
            .collect()
    }
}

