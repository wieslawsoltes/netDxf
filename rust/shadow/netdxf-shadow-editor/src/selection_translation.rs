use crate::{AffineEditOperations, AffineEditOptions, AffineEntityEdit};
use netdxf_shadow::{Affine3, ObjectId, ShadowEditBatch, ShadowSnapshot, Vec3};
use netdxf_shadow_rendering::RenderScene;
use std::collections::BTreeSet;

/// Conjugates display-space selection translations through paper/model/insert coordinates
/// (`SelectionTranslation.cs`).
pub struct SelectionTranslation;

impl SelectionTranslation {
    /// Plans a translation batch for the selected render item keys in `scene`.
    pub fn plan(
        snapshot: &ShadowSnapshot,
        scene: &RenderScene,
        selection: &[String],
        world_delta: Vec3,
        edit_block_definition: bool,
    ) -> Result<ShadowEditBatch, String> {
        if snapshot.document_id != scene.document_id || snapshot.revision != scene.revision {
            return Err("Selection belongs to an obsolete rendered snapshot.".into());
        }
        let keys: BTreeSet<&str> = selection.iter().map(String::as_str).collect();
        let transform = Affine3::move_by(world_delta);
        let mut targets: Vec<AffineEntityEdit> = Vec::new();
        let mut seen: BTreeSet<ObjectId> = BTreeSet::new();

        for item in &scene.items {
            if !keys.contains(item.key.as_str()) {
                continue;
            }
            let target_id = if !edit_block_definition && !item.insert_path.is_empty() {
                item.insert_path[0].clone()
            } else {
                item.entity.clone()
            };
            if !seen.insert(target_id.clone()) {
                continue;
            }
            targets.push(AffineEntityEdit {
                entity: target_id,
                transform,
            });
        }

        Ok(AffineEditOperations::transform_entities(
            snapshot,
            &targets,
            &AffineEditOptions::default(),
        ))
    }
}
