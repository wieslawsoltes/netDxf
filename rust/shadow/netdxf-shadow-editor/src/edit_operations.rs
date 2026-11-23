#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, IShadowGraphStore, ObjectId, ShadowEditBatch, ShadowGraphPatch, ShadowResult, ShadowSnapshot, ShadowValue, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::sync::Arc;

pub struct EditOperations;

impl EditOperations {
    pub fn translate(
        snapshot: &ShadowSnapshot,
        entities: &[ObjectId],
        delta: Vec3,
    ) -> ShadowEditBatch {
        Self::move_entities(snapshot, entities, delta)
    }

    pub fn move_entities(
        snapshot: &ShadowSnapshot,
        entities: &[ObjectId],
        delta: Vec3,
    ) -> ShadowEditBatch {
        let affine = Affine3::move_by(delta);
        let edits: Vec<AffineEntityEdit> = entities
            .iter()
            .map(|id| AffineEntityEdit {
                entity: id.clone(),
                transform: affine,
            })
            .collect();
        AffineEditOperations::transform_entities(snapshot, &edits, &AffineEditOptions::default())
    }
}

