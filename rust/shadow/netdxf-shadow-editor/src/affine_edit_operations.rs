#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, IShadowGraphStore, ObjectId, ShadowEditBatch, ShadowGraphPatch, ShadowResult, ShadowSnapshot, ShadowValue, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::sync::Arc;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct AffineEditOptions {
    pub preserve_non_uniform_scale: bool,
    pub tolerance: f64,
}

impl Default for AffineEditOptions {
    fn default() -> Self {
        Self {
            preserve_non_uniform_scale: true,
            tolerance: 1e-9,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AffineEntityEdit {
    pub entity: ObjectId,
    pub transform: Affine3,
}

pub struct AffineEditOperations;

impl AffineEditOperations {
    /// Builds a property edit batch transforming `StartPoint`/`EndPoint`/`Center`/`Position` by `edit.transform`.
    pub fn transform_entities(
        snapshot: &ShadowSnapshot,
        edits: &[AffineEntityEdit],
        _options: &AffineEditOptions,
    ) -> ShadowEditBatch {
        let mut prop_edits = Vec::new();
        for edit in edits {
            let reader = snapshot.read(edit.entity.clone());
            for prop in ["StartPoint", "EndPoint", "Center", "Position"] {
                let f = reader.field(prop);
                if f.exists() {
                    let before_v = f.vector(Vec3::ZERO);
                    let after_v = edit.transform.point(before_v);
                    prop_edits.push(netdxf_shadow::ShadowPropertyEdit {
                        object: edit.entity.clone(),
                        property: prop.to_string(),
                        before: f.value.clone(),
                        after: ShadowValue::from_vec3(after_v, false),
                    });
                }
            }
        }
        ShadowEditBatch::create(snapshot, prop_edits)
    }
}

