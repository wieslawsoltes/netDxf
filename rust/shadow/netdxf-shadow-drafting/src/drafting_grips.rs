#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

pub struct DraftingGrips;

impl DraftingGrips {
    pub fn extract(snapshot: &netdxf_shadow::ShadowSnapshot, entity: &ObjectId) -> Vec<DraftingGrip> {
        let r = snapshot.read(entity.clone());
        let mut grips = Vec::new();
        for key in ["StartPoint", "EndPoint", "Center", "Position"] {
            let f = r.field(key);
            if f.exists() {
                grips.push(DraftingGrip {
                    entity: entity.clone(),
                    key: key.to_string(),
                    position: f.vector(Vec3::ZERO),
                });
            }
        }
        grips
    }
}

