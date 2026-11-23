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
    pub maximum_details: usize,
    pub maximum_objects: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShadowPreviewChange {
    pub object: ObjectId,
    pub property: String,
    pub kind: String,
    pub r#type: String,
    pub properties: Vec<String>,
    pub before: ShadowValue,
    pub after: ShadowValue,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShadowPreviewSummary {
    pub added: usize,
    pub modified: usize,
    pub changed: usize,
    pub removed: usize,
    pub details_truncated: bool,
    pub details: Vec<ShadowPreviewChange>,
    pub changes: Vec<ShadowPreviewChange>,
}

#[derive(Debug, Clone, PartialEq, Default)]
pub struct ShadowEditPreview {
    pub description: String,
    pub target: Option<ObjectId>,
    pub patch: Option<ShadowGraphPatch>,
    pub proposed: Option<Arc<ShadowSnapshot>>,
    pub summary: ShadowPreviewSummary,
    pub validation: Vec<String>,
}

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
                                kind: "modified".into(),
                                r#type: after.type_name.clone(),
                                properties: vec![k.clone()],
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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct EditHistoryEntry {
    pub label: String,
    pub description: String,
    pub revision: i64,
    pub before: i64,
    pub after: i64,
    pub changes: usize,
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
        let before_rev = before.revision;
        let label_str = label.into();
        self.undo_stack.push(before);
        self.redo_stack.clear();
        self.history.push(EditHistoryEntry {
            label: label_str.clone(),
            description: label_str,
            revision: after.revision,
            before: before_rev,
            after: after.revision,
            changes: batch.edits.len(),
        });
        Ok(after)
    }

    pub fn apply_patch(
        &mut self,
        label: impl Into<String>,
        patch: &ShadowGraphPatch,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        let before = self.store.current();
        let before_rev = before.revision;
        let after = self.store.apply_patch(patch)?;
        let label_str = label.into();
        self.undo_stack.push(before);
        self.redo_stack.clear();
        self.history.push(EditHistoryEntry {
            label: label_str.clone(),
            description: label_str,
            revision: after.revision,
            before: before_rev,
            after: after.revision,
            changes: patch.changes.len(),
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

    pub fn select(&mut self, ids: Vec<ObjectId>) {
        self.selection = ids;
    }

    pub fn reset_history(&mut self) {
        self.undo_stack.clear();
        self.redo_stack.clear();
        self.history.clear();
    }

    pub fn preview(
        &self,
        patch: &ShadowGraphPatch,
        options: &ShadowPreviewOptions,
    ) -> ShadowPreviewSummary {
        ShadowEditPreview::summarize(patch, options)
    }

    pub fn apply_preview(
        &mut self,
        label: impl Into<String>,
        patch: &ShadowGraphPatch,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        self.apply_patch(label, patch)
    }

    pub fn preview_merge(
        &self,
        incoming: &ShadowSnapshot,
        options: &ShadowPreviewOptions,
    ) -> ShadowResult<ShadowPreviewSummary> {
        let cur = self.store.current();
        let patch = ShadowGraphPatch::between(&cur, incoming)?;
        Ok(ShadowEditPreview::summarize(&patch, options))
    }

    pub fn apply_merge(
        &mut self,
        label: impl Into<String>,
        incoming: &ShadowSnapshot,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        let cur = self.store.current();
        let patch = ShadowGraphPatch::between(&cur, incoming)?;
        self.apply_patch(label, &patch)
    }

    pub fn create_entity(
        &mut self,
        type_name: impl Into<String>,
        properties: std::collections::BTreeMap<String, ShadowValue>,
    ) -> ShadowResult<(ObjectId, Arc<ShadowSnapshot>)> {
        let cur = self.store.current();
        let new_id = ObjectId::new(format!("E_{}", cur.objects.len() + 1));
        let mut obj = netdxf_shadow::ShadowObject::new(new_id.clone(), type_name);
        obj.properties = properties;
        let patch = ShadowGraphPatch::new(
            cur.document_id.clone(),
            cur.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: new_id.clone(),
                before: None,
                after: Some(obj),
            }],
        );
        let after = self.apply_patch("create_entity", &patch)?;
        Ok((new_id, after))
    }

    pub fn delete_entities(&mut self, ids: &[ObjectId]) -> ShadowResult<Arc<ShadowSnapshot>> {
        let cur = self.store.current();
        let mut changes = Vec::new();
        for id in ids {
            if let Some(existing) = cur.objects.get(id) {
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**existing).clone()),
                    after: None,
                });
            }
        }
        let patch = ShadowGraphPatch::new(cur.document_id.clone(), cur.revision, changes);
        self.selection.retain(|id| !ids.contains(id));
        self.apply_patch("delete_entities", &patch)
    }

    pub fn replace_collection(
        &mut self,
        label: impl Into<String>,
        patch: &ShadowGraphPatch,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        self.apply_patch(label, patch)
    }

    pub fn execute(
        &mut self,
        label: impl Into<String>,
        batch: &ShadowEditBatch,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        self.apply_batch(label, batch)
    }

    pub fn snapshot(&self) -> Arc<ShadowSnapshot> {
        self.current()
    }

    pub fn undo_descriptions(&self) -> Vec<String> {
        self.history.iter().map(|e| e.label.clone()).collect()
    }

    pub fn toggle_selection(&mut self, id: ObjectId) {
        if let Some(pos) = self.selection.iter().position(|x| x == &id) {
            self.selection.remove(pos);
        } else {
            self.selection.push(id);
        }
    }

    pub fn set_property(
        &mut self,
        id: &ObjectId,
        property: impl Into<String>,
        value: ShadowValue,
    ) -> ShadowResult<Arc<ShadowSnapshot>> {
        let cur = self.store.current();
        let prop = property.into();
        let existing = cur.objects.get(id).ok_or_else(|| {
            netdxf_shadow::ShadowConflictException::InvalidArgument(format!(
                "Unknown object '{}'",
                id.as_str()
            ))
        })?;
        let mut updated = (**existing).clone();
        updated.properties.insert(prop.clone(), value);
        let patch = ShadowGraphPatch::new(
            cur.document_id.clone(),
            cur.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: id.clone(),
                before: Some((**existing).clone()),
                after: Some(updated),
            }],
        );
        self.apply_patch(format!("Set {}.{}", existing.type_name, prop), &patch)
    }

    pub fn translate(&mut self, delta: Vec3) -> ShadowResult<Arc<ShadowSnapshot>> {
        let cur = self.store.current();
        let mut edits = Vec::new();
        for id in &self.selection {
            if cur.objects.contains_key(id) {
                let reader = cur.read(id.clone());
                for key in ["StartPoint", "EndPoint", "Center", "Position", "Location"] {
                    let field = reader.field(key);
                    if let ShadowValue::Structure(_) = field.value {
                        let v = field.vector(Vec3::ZERO);
                        edits.push(netdxf_shadow::ShadowPropertyEdit {
                            object: id.clone(),
                            property: key.to_string(),
                            before: field.value.clone(),
                            after: ShadowValue::from_vec3(v + delta, false),
                        });
                    }
                }
            }
        }
        let batch = ShadowEditBatch::create(&cur, edits);
        self.apply_batch("Translate selection", &batch)
    }

    pub fn translate_perspective(&mut self, delta: Vec3) -> ShadowResult<Arc<ShadowSnapshot>> {
        self.translate(delta)
    }

    pub fn transform(&mut self, matrix: &Affine3) -> ShadowResult<Arc<ShadowSnapshot>> {
        let cur = self.store.current();
        let mut edits = Vec::new();
        for id in &self.selection {
            if cur.objects.contains_key(id) {
                let reader = cur.read(id.clone());
                for key in ["StartPoint", "EndPoint", "Center", "Position", "Location"] {
                    let field = reader.field(key);
                    if let ShadowValue::Structure(_) = field.value {
                        let v = field.vector(Vec3::ZERO);
                        edits.push(netdxf_shadow::ShadowPropertyEdit {
                            object: id.clone(),
                            property: key.to_string(),
                            before: field.value.clone(),
                            after: ShadowValue::from_vec3(matrix.point(v), false),
                        });
                    }
                }
            }
        }
        let batch = ShadowEditBatch::create(&cur, edits);
        self.apply_batch("Transform selection", &batch)
    }

    pub fn synchronize_source(&mut self) -> Arc<ShadowSnapshot> {
        self.reset_history();
        self.store.current()
    }
}

