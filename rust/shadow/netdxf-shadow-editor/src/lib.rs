//! Interactive CAD editor session, undo/redo history, snapping, and edit operations (`netDxf.Shadow.Editor`).

#![forbid(unsafe_code)]

use netdxf_shadow::{
    Affine3, IShadowGraphStore, ObjectId, ShadowEditBatch, ShadowGraphPatch, ShadowResult,
    ShadowSnapshot, ShadowValue, Vec3,
};
use serde::{Deserialize, Serialize};
use std::sync::Arc;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum SnapMode {
    #[default]
    None,
    Grid,
    Endpoint,
    Midpoint,
    Center,
    Nearest,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct SnapResult {
    pub point: Vec3,
    pub mode: SnapMode,
    pub distance: f64,
}

pub struct Snapping;

impl Snapping {
    pub fn snap_to_grid(point: Vec3, spacing: f64) -> SnapResult {
        let s = spacing.max(1e-9);
        let snapped = Vec3::new(
            (point.x / s).round() * s,
            (point.y / s).round() * s,
            (point.z / s).round() * s,
        );
        SnapResult {
            point: snapped,
            mode: SnapMode::Grid,
            distance: Vec3::distance(point, snapped),
        }
    }

    pub fn snap_to_endpoints(point: Vec3, candidates: &[Vec3], tolerance: f64) -> Option<SnapResult> {
        let mut best: Option<SnapResult> = None;
        for &c in candidates {
            let d = Vec3::distance(point, c);
            if d <= tolerance && best.map_or(true, |b| d < b.distance) {
                best = Some(SnapResult {
                    point: c,
                    mode: SnapMode::Endpoint,
                    distance: d,
                });
            }
        }
        best
    }
}

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

pub struct CustomHeaderEdits;

impl CustomHeaderEdits {
    pub fn set_property(
        snapshot: &ShadowSnapshot,
        name: impl Into<String>,
        value: impl Into<String>,
    ) -> ShadowGraphPatch {
        let mut edit = netdxf_shadow::ShadowGraphEdit::new(snapshot.clone());
        let _ = edit.set(&snapshot.root, name, ShadowValue::from_string(value));
        edit.build()
    }
}

pub struct EditOperations;

impl EditOperations {
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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShadowPreviewOptions {
    pub maximum_changes: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowPreviewChange {
    pub object: ObjectId,
    pub property: String,
    pub before: ShadowValue,
    pub after: ShadowValue,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShadowPreviewSummary {
    pub added: usize,
    pub modified: usize,
    pub removed: usize,
    pub changes: Vec<ShadowPreviewChange>,
}

pub struct ShadowEditPreview;

impl ShadowEditPreview {
    pub fn summarize(patch: &ShadowGraphPatch, options: &ShadowPreviewOptions) -> ShadowPreviewSummary {
        let mut summary = ShadowPreviewSummary::default();
        let limit = if options.maximum_changes == 0 {
            usize::MAX
        } else {
            options.maximum_changes
        };
        for change in &patch.changes {
            match (&change.before, &change.after) {
                (None, Some(_)) => summary.added += 1,
                (Some(_), None) => summary.removed += 1,
                (Some(before), Some(after)) => {
                    summary.modified += 1;
                    for (k, av) in &after.properties {
                        if summary.changes.len() >= limit {
                            break;
                        }
                        let bv = before.get(k);
                        if bv != *av {
                            summary.changes.push(ShadowPreviewChange {
                                object: change.id.clone(),
                                property: k.clone(),
                                before: bv,
                                after: av.clone(),
                            });
                        }
                    }
                }
                (None, None) => {}
            }
        }
        summary
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EditHistoryEntry {
    pub label: String,
    pub revision: i64,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct EditorState {
    pub revision: i64,
    pub can_undo: bool,
    pub can_redo: bool,
    pub selection: Vec<ObjectId>,
    pub history: Vec<EditHistoryEntry>,
}

/// Interactive undo/redo editor session over an [`IShadowGraphStore`].
pub struct EditorSession {
    store: Box<dyn IShadowGraphStore>,
    undo_stack: Vec<Arc<ShadowSnapshot>>,
    redo_stack: Vec<Arc<ShadowSnapshot>>,
    history: Vec<EditHistoryEntry>,
    pub selection: Vec<ObjectId>,
}

impl EditorSession {
    pub fn new(store: Box<dyn IShadowGraphStore>) -> Self {
        Self {
            store,
            undo_stack: Vec::new(),
            redo_stack: Vec::new(),
            history: Vec::new(),
            selection: Vec::new(),
        }
    }

    pub fn current(&self) -> Arc<ShadowSnapshot> {
        self.store.current()
    }

    pub fn state(&self) -> EditorState {
        EditorState {
            revision: self.store.current().revision,
            can_undo: !self.undo_stack.is_empty(),
            can_redo: !self.redo_stack.is_empty(),
            selection: self.selection.clone(),
            history: self.history.clone(),
        }
    }

    pub fn apply_batch(
        &mut self,
        label: impl Into<String>,
        batch: &ShadowEditBatch,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        let before = self.store.current();
        let after = self.store.apply(batch)?;
        self.undo_stack.push(before);
        self.redo_stack.clear();
        self.history.push(EditHistoryEntry {
            label: label.into(),
            revision: after.revision,
        });
        Ok(after)
    }

    pub fn apply_patch(
        &mut self,
        label: impl Into<String>,
        patch: &ShadowGraphPatch,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        let before = self.store.current();
        let after = self.store.apply_patch(patch)?;
        self.undo_stack.push(before);
        self.redo_stack.clear();
        self.history.push(EditHistoryEntry {
            label: label.into(),
            revision: after.revision,
        });
        Ok(after)
    }

    pub fn undo(&mut self) -> ShadowResult<Option<Arc<ShadowSnapshot>>> {
        let Some(prev) = self.undo_stack.pop() else {
            return Ok(None);
        };
        let cur = self.store.current();
        let patch = ShadowGraphPatch::between(&cur, &prev)?;
        let restored = self.store.apply_patch(&patch)?;
        self.redo_stack.push(cur);
        Ok(Some(restored))
    }

    pub fn redo(&mut self) -> ShadowResult<Option<Arc<ShadowSnapshot>>> {
        let Some(next) = self.redo_stack.pop() else {
            return Ok(None);
        };
        let cur = self.store.current();
        let patch = ShadowGraphPatch::between(&cur, &next)?;
        let restored = self.store.apply_patch(&patch)?;
        self.undo_stack.push(cur);
        Ok(Some(restored))
    }
}
