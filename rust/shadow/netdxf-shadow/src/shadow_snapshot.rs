use crate::math::Vec3;
use crate::shadow_graph_patch::{IShadowGraphStore, ShadowGraphPatch};
use crate::shadow_read_set::IShadowReadObserver;
use crate::shadow_value::{ObjectId, ShadowDiagnostic, ShadowObject, ShadowValue};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::sync::{Arc, RwLock};
use thiserror::Error;

/// Unified error type for `netdxf-shadow` optimistic edits, patches, and merges.
#[derive(Debug, Clone, Error, PartialEq, Eq)]
pub enum ShadowConflictException {
    #[error("shadow conflict: {0}")]
    Conflict(String),
    #[error("invalid argument: {0}")]
    InvalidArgument(String),
    #[error("invalid operation: {0}")]
    InvalidOperation(String),
}

pub type ShadowResult<T> = std::result::Result<T, ShadowConflictException>;

/// Optimistic property-level edit against a specific snapshot revision.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowPropertyEdit {
    pub object: ObjectId,
    pub property: String,
    pub before: ShadowValue,
    pub after: ShadowValue,
}

/// Atomic batch of [`ShadowPropertyEdit`]s for a specific document and revision.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowEditBatch {
    pub document_id: String,
    pub expected_revision: i64,
    pub edits: Vec<ShadowPropertyEdit>,
}

impl ShadowEditBatch {
    pub fn create(snapshot: &ShadowSnapshot, edits: Vec<ShadowPropertyEdit>) -> Self {
        Self {
            document_id: snapshot.document_id.clone(),
            expected_revision: snapshot.revision,
            edits,
        }
    }

    pub fn inverse(&self, current: &ShadowSnapshot) -> Self {
        let reversed = self
            .edits
            .iter()
            .rev()
            .map(|e| ShadowPropertyEdit {
                object: e.object.clone(),
                property: e.property.clone(),
                before: e.after.clone(),
                after: e.before.clone(),
            })
            .collect();
        Self {
            document_id: current.document_id.clone(),
            expected_revision: current.revision,
            edits: reversed,
        }
    }
}

/// Summary of added, changed, and removed [`ObjectId`]s between two revisions.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowDelta {
    pub from_revision: i64,
    pub to_revision: i64,
    pub added: BTreeSet<ObjectId>,
    pub changed: BTreeSet<ObjectId>,
    pub removed: BTreeSet<ObjectId>,
}

/// Immutable document graph snapshot; cloning/retaining a snapshot shares `Arc<ShadowObject>` nodes.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShadowSnapshot {
    pub document_id: String,
    pub revision: i64,
    pub root: ObjectId,
    pub objects: BTreeMap<ObjectId, Arc<ShadowObject>>,
    pub entity_ids: BTreeSet<ObjectId>,
    pub diagnostics: Vec<ShadowDiagnostic>,
}

impl ShadowSnapshot {
    pub fn new(
        document_id: impl Into<String>,
        revision: i64,
        root: ObjectId,
        objects: BTreeMap<ObjectId, Arc<ShadowObject>>,
        entity_ids: BTreeSet<ObjectId>,
        diagnostics: Vec<ShadowDiagnostic>,
    ) -> Self {
        Self {
            document_id: document_id.into(),
            revision,
            root,
            objects,
            entity_ids,
            diagnostics,
        }
    }

    pub fn read(&self, id: ObjectId) -> ShadowReader<'_> {
        ShadowReader::new(self, ShadowValue::reference(id))
    }

    pub fn document(&self) -> ShadowReader<'_> {
        self.read(self.root.clone())
    }

    pub fn layouts(&self) -> Vec<ShadowReader<'_>> {
        self.document().field("Layouts").items()
    }

    pub fn layout(&self, name: &str) -> Option<ShadowReader<'_>> {
        self.layouts()
            .into_iter()
            .find(|n| n.field("Name").text().eq_ignore_ascii_case(name))
    }

    pub fn drawing_entities(&self, layout: &str) -> Vec<ShadowReader<'_>> {
        self.layout(layout)
            .map(|l| l.field("AssociatedBlock").field("Entities").items())
            .unwrap_or_default()
    }

    pub fn apply(&self, batch: &ShadowEditBatch) -> ShadowResult<Self> {
        if batch.document_id != self.document_id || batch.expected_revision != self.revision {
            return Err(ShadowConflictException::Conflict(
                "The edit belongs to another document or a stale snapshot.".into(),
            ));
        }
        if batch.edits.is_empty() {
            return Ok(self.clone());
        }
        let mut seen = BTreeSet::new();
        let mut objects = self.objects.clone();
        let mut changed_ids = BTreeSet::new();

        for edit in &batch.edits {
            if !seen.insert((edit.object.clone(), edit.property.clone())) {
                return Err(ShadowConflictException::InvalidArgument(
                    "Duplicate property edits are not allowed.".into(),
                ));
            }
            let Some(node) = objects.get(&edit.object) else {
                return Err(ShadowConflictException::Conflict(format!(
                    "Object {} was removed.",
                    edit.object
                )));
            };
            if !node.writable_properties.contains(&edit.property) {
                return Err(ShadowConflictException::InvalidOperation(format!(
                    "{}.{} is read-only.",
                    node.short_type(),
                    edit.property
                )));
            }
            if !ShadowValue::equivalent(&node.get(&edit.property), &edit.before) {
                return Err(ShadowConflictException::Conflict(format!(
                    "{}.{} has changed.",
                    edit.object, edit.property
                )));
            }
            for reference in edit.after.references() {
                if !self.objects.contains_key(&reference) {
                    return Err(ShadowConflictException::InvalidArgument(format!(
                        "Dangling reference: {reference}."
                    )));
                }
            }
            if !ShadowValue::equivalent(&node.get(&edit.property), &edit.after) {
                let mut updated = (**node).clone();
                updated
                    .properties
                    .insert(edit.property.clone(), edit.after.clone());
                objects.insert(edit.object.clone(), Arc::new(updated));
                changed_ids.insert(edit.object.clone());
            }
        }

        if changed_ids.is_empty() {
            return Ok(self.clone());
        }

        Ok(Self {
            document_id: self.document_id.clone(),
            revision: self.revision.checked_add(1).ok_or_else(|| {
                ShadowConflictException::InvalidOperation("Revision overflow.".into())
            })?,
            root: self.root.clone(),
            objects,
            entity_ids: self.entity_ids.clone(),
            diagnostics: self.diagnostics.clone(),
        })
    }

    pub fn diff(&self, previous: &Self) -> ShadowResult<ShadowDelta> {
        if self.document_id != previous.document_id {
            return Err(ShadowConflictException::InvalidArgument(
                "Cannot diff unrelated documents.".into(),
            ));
        }
        let mut added = BTreeSet::new();
        let mut changed = BTreeSet::new();
        let mut removed = BTreeSet::new();

        for (id, node) in &self.objects {
            match previous.objects.get(id) {
                None => {
                    added.insert(id.clone());
                }
                Some(before) if !Arc::ptr_eq(node, before) && node != before => {
                    changed.insert(id.clone());
                }
                _ => {}
            }
        }
        for id in previous.objects.keys() {
            if !self.objects.contains_key(id) {
                removed.insert(id.clone());
            }
        }

        Ok(ShadowDelta {
            from_revision: previous.revision,
            to_revision: self.revision,
            added,
            changed,
            removed,
        })
    }

    /// Reverse references for resource/block invalidation.
    pub fn build_dependents(&self) -> BTreeMap<ObjectId, BTreeSet<ObjectId>> {
        let mut map: BTreeMap<ObjectId, BTreeSet<ObjectId>> = BTreeMap::new();
        for node in self.objects.values() {
            for value in node.properties.values() {
                for target in value.references() {
                    map.entry(target).or_default().insert(node.id.clone());
                }
            }
        }
        map
    }
}

/// Lightweight read cursor over an immutable [`ShadowSnapshot`].
#[derive(Debug, Clone)]
pub struct ShadowReader<'a> {
    pub snapshot: &'a ShadowSnapshot,
    pub value: ShadowValue,
    resolved: Option<&'a Arc<ShadowObject>>,
}

impl<'a> ShadowReader<'a> {
    pub fn new(snapshot: &'a ShadowSnapshot, value: ShadowValue) -> Self {
        let resolved = match &value {
            ShadowValue::Reference(r) => snapshot.objects.get(&r.id),
            _ => None,
        };
        Self {
            snapshot,
            value,
            resolved,
        }
    }

    pub fn resolve(&self, id: ObjectId) -> Self {
        Self::new(self.snapshot, ShadowValue::reference(id))
    }

    pub fn id(&self) -> Option<&ObjectId> {
        match &self.value {
            ShadowValue::Reference(r) => Some(&r.id),
            _ => None,
        }
    }

    pub fn object(&self) -> Option<&'a Arc<ShadowObject>> {
        self.resolved
    }

    pub fn object_observed(&self, observer: &mut dyn IShadowReadObserver) -> Option<&'a Arc<ShadowObject>> {
        let id = self.id()?;
        observer.read(id, self.resolved);
        self.resolved
    }

    pub fn observe(&self, observer: &mut dyn IShadowReadObserver) -> Option<&'a Arc<ShadowObject>> {
        self.object_observed(observer)
    }

    #[inline]
    pub fn r#type(&self) -> &str {
        self.type_name()
    }

    pub fn exists(&self) -> bool {
        !matches!(self.value, ShadowValue::Null(_))
    }

    pub fn type_name(&self) -> &str {
        if let Some(obj) = self.resolved {
            obj.short_type()
        } else if let ShadowValue::Structure(s) = &self.value {
            match s.type_name.rfind('.') {
                Some(idx) => &s.type_name[idx + 1..],
                None => &s.type_name,
            }
        } else {
            ""
        }
    }

    pub fn text(&self) -> &str {
        match &self.value {
            ShadowValue::Atom(a) => &a.text,
            _ => "",
        }
    }

    pub fn number(&self, fallback: f64) -> f64 {
        self.text().parse::<f64>().unwrap_or(fallback)
    }

    pub fn boolean(&self, fallback: bool) -> bool {
        match self.text().to_ascii_lowercase().as_str() {
            "true" => true,
            "false" => false,
            _ => fallback,
        }
    }

    pub fn vector(&self, fallback: Vec3) -> Vec3 {
        if self.field("X").exists() {
            Vec3::new(
                self.field("X").number(0.0),
                self.field("Y").number(0.0),
                self.field("Z").number(0.0),
            )
        } else {
            fallback
        }
    }

    pub fn field(&self, key: &str) -> Self {
        let next = if let Some(obj) = self.resolved {
            obj.get(key)
        } else if let ShadowValue::Structure(s) = &self.value {
            s.fields
                .get(key)
                .cloned()
                .unwrap_or_else(ShadowValue::null)
        } else {
            ShadowValue::null()
        };
        Self::new(self.snapshot, next)
    }

    pub fn items(&self) -> Vec<Self> {
        let seq = match &self.value {
            ShadowValue::Sequence(s) => Some(s.items.clone()),
            _ => match self.field("$items").value {
                ShadowValue::Sequence(s) => Some(s.items),
                _ => None,
            },
        };
        seq.unwrap_or_default()
            .into_iter()
            .map(|v| Self::new(self.snapshot, v))
            .collect()
    }
}

/// Store interface for optimistic property-level shadow batches.
pub trait IShadowStore: Send + Sync {
    fn current(&self) -> Arc<ShadowSnapshot>;
    fn apply(&self, batch: &ShadowEditBatch) -> ShadowResult<Arc<ShadowSnapshot>>;
}

/// Thread-safe in-memory immutable editor branch store.
#[derive(Debug)]
pub struct ShadowMemoryStore {
    current: RwLock<Arc<ShadowSnapshot>>,
}

impl ShadowMemoryStore {
    pub fn new(initial: ShadowSnapshot) -> Self {
        Self {
            current: RwLock::new(Arc::new(initial)),
        }
    }
}

impl IShadowStore for ShadowMemoryStore {
    fn current(&self) -> Arc<ShadowSnapshot> {
        self.current
            .read()
            .map(|g| Arc::clone(&g))
            .unwrap_or_else(|e| Arc::clone(&e.into_inner()))
    }

    fn apply(&self, batch: &ShadowEditBatch) -> ShadowResult<Arc<ShadowSnapshot>> {
        let mut guard = self
            .current
            .write()
            .unwrap_or_else(|e| e.into_inner());
        let next = Arc::new(guard.apply(batch)?);
        *guard = Arc::clone(&next);
        Ok(next)
    }
}

impl IShadowGraphStore for ShadowMemoryStore {
    fn apply_patch(&self, patch: &ShadowGraphPatch) -> ShadowResult<Arc<ShadowSnapshot>> {
        let mut guard = self
            .current
            .write()
            .unwrap_or_else(|e| e.into_inner());
        let next = Arc::new(patch.apply(&guard)?);
        *guard = Arc::clone(&next);
        Ok(next)
    }
}
