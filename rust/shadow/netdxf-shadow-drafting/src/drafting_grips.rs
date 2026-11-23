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
    pub fn read(snapshot: &netdxf_shadow::ShadowSnapshot, entity: &ObjectId) -> Vec<DraftingGrip> {
        Self::extract(snapshot, entity)
    }

    pub fn r#move(
        snapshot: &netdxf_shadow::ShadowSnapshot,
        grip: &DraftingGrip,
        target: Vec3,
    ) -> DraftingPlan {
        let mut plan = DraftingPlanner::empty_plan(snapshot, format!("GRIP MOVE {}", grip.key));
        plan.command.points = vec![grip.position, target];
        plan
    }

    pub fn extract(snapshot: &netdxf_shadow::ShadowSnapshot, entity: &ObjectId) -> Vec<DraftingGrip> {
        let r = snapshot.read(entity.clone());
        let mut grips = Vec::new();
        for key in ["StartPoint", "EndPoint", "Center", "Position"] {
            let f = r.field(key);
            if f.exists() {
                grips.push(DraftingGrip {
                    id: format!("{}:{}", entity.as_str(), key),
                    entity: entity.clone(),
                    key: key.to_string(),
                    property: key.to_string(),
                    kind: "Point".to_string(),
                    index: grips.len(),
                    position: f.vector(Vec3::ZERO),
                });
            }
        }
        grips
    }
}

