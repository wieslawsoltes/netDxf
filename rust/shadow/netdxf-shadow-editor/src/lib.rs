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
pub struct CustomHeaderEdits;
pub struct EditOperations;

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
}
