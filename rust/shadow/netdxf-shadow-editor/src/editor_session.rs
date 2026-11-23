#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, IShadowGraphStore, ObjectId, ShadowEditBatch, ShadowGraphPatch, ShadowResult, ShadowSnapshot, ShadowValue, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::sync::Arc;

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

