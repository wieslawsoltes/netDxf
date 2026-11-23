#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowGraphPatch, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum DraftingSelectionMode {
    #[default]
    Window,
    Crossing,
    Point,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ObjectSnapMode {
    #[default]
    Endpoint,
    Midpoint,
    Center,
    Intersection,
    Perpendicular,
    Nearest,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ObjectSnap {
    pub mode: ObjectSnapMode,
    pub point: Vec3,
    pub entity: Option<ObjectId>,
}

pub struct DraftingQueries;

impl DraftingQueries {
    pub fn select_in_bounds(
        scene: &netdxf_shadow_geometry::CadScene,
        region: netdxf_shadow::Bounds2,
        mode: DraftingSelectionMode,
    ) -> Vec<ObjectId> {
        scene
            .occurrences
            .iter()
            .filter(|occ| match mode {
                DraftingSelectionMode::Window => {
                    !occ.bounds.is_empty()
                        && occ.bounds.min_x >= region.min_x
                        && occ.bounds.max_x <= region.max_x
                        && occ.bounds.min_y >= region.min_y
                        && occ.bounds.max_y <= region.max_y
                }
                DraftingSelectionMode::Crossing | DraftingSelectionMode::Point => {
                    occ.bounds.intersects(region)
                }
            })
            .map(|occ| occ.entity.clone())
            .collect()
    }
}

