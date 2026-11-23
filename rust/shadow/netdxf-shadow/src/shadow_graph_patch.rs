use crate::shadow_snapshot::{IShadowStore, ShadowConflictException, ShadowResult, ShadowSnapshot};
use crate::shadow_value::{
    ObjectId, ShadowObject, ShadowObjectKind, ShadowValue,
};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

static NEXT_NODE_ID: AtomicU64 = AtomicU64::new(1);

/// An optimistic, immutable replacement of one public object-graph node.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowObjectChange {
    pub id: ObjectId,
    pub before: Option<ShadowObject>,
    pub after: Option<ShadowObject>,
}

/// Structural transaction over a [`ShadowSnapshot`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ShadowGraphPatch {
    pub document_id: String,
    pub expected_revision: i64,
    pub changes: Vec<ShadowObjectChange>,
    pub historical_target: Option<Arc<ShadowSnapshot>>,
}

impl ShadowGraphPatch {
    pub fn new(
        document_id: impl Into<String>,
        expected_revision: i64,
        changes: Vec<ShadowObjectChange>,
    ) -> Self {
        Self {
            document_id: document_id.into(),
            expected_revision,
            changes,
            historical_target: None,
        }
    }

    pub fn between(before: &ShadowSnapshot, after: &ShadowSnapshot) -> ShadowResult<Self> {
        if before.document_id != after.document_id || before.root != after.root {
            return Err(ShadowConflictException::InvalidArgument(
                "Cannot reconcile unrelated document graphs.".into(),
            ));
        }
        let mut changes = Vec::new();
        for (id, node) in &before.objects {
            match after.objects.get(id) {
                None => changes.push(ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**node).clone()),
                    after: None,
                }),
                Some(target) if !Self::equivalent(Some(node), Some(target)) => {
                    changes.push(ShadowObjectChange {
                        id: id.clone(),
                        before: Some((**node).clone()),
                        after: Some((**target).clone()),
                    });
                }
                _ => {}
            }
        }
        for (id, node) in &after.objects {
            if !before.objects.contains_key(id) {
                changes.push(ShadowObjectChange {
                    id: id.clone(),
                    before: None,
                    after: Some((**node).clone()),
                });
            }
        }
        Ok(Self {
            document_id: before.document_id.clone(),
            expected_revision: before.revision,
            changes,
            historical_target: Some(Arc::new(after.clone())),
        })
    }

    pub fn apply(&self, snapshot: &ShadowSnapshot) -> ShadowResult<ShadowSnapshot> {
        if snapshot.document_id != self.document_id || snapshot.revision != self.expected_revision {
            return Err(ShadowConflictException::Conflict(
                "Structural edit belongs to a stale snapshot or another document.".into(),
            ));
        }
        if self.changes.is_empty() {
            return Ok(snapshot.clone());
        }

        let mut seen = BTreeSet::new();
        let mut nodes = snapshot.objects.clone();
        let mut entities = snapshot.entity_ids.clone();
        let mut changed = false;
        let mut has_removal = false;

        for edit in &self.changes {
            if edit.id.is_empty() || !seen.insert(edit.id.clone()) {
                return Err(ShadowConflictException::InvalidArgument(
                    "Structural edits require unique, nonempty object identities.".into(),
                ));
            }
            let actual = snapshot.objects.get(&edit.id).map(|a| a.as_ref());
            if !Self::equivalent(actual, edit.before.as_ref()) {
                return Err(ShadowConflictException::Conflict(format!(
                    "Structural precondition failed for {}.",
                    edit.id
                )));
            }
            if let Some(ref replacement) = edit.after {
                if replacement.id != edit.id {
                    return Err(ShadowConflictException::InvalidArgument(
                        "Object identity and patch identity must agree.".into(),
                    ));
                }
            }
            if let Some(ref original) = edit.before {
                if original.id != edit.id {
                    return Err(ShadowConflictException::InvalidArgument(
                        "Object identity and patch identity must agree.".into(),
                    ));
                }
            }
            if edit.id == snapshot.root {
                let root_ok = edit
                    .after
                    .as_ref()
                    .is_some_and(|a| actual.is_some_and(|act| a.type_name == act.type_name));
                if !root_ok {
                    return Err(ShadowConflictException::InvalidArgument(
                        "The document root cannot be removed or replaced with another type.".into(),
                    ));
                }
            }
            if let (Some(b), Some(a)) = (&edit.before, &edit.after) {
                if b.type_name != a.type_name {
                    return Err(ShadowConflictException::InvalidArgument(
                        "Changing the runtime type of an existing identity is not supported."
                            .into(),
                    ));
                }
            }
            if Self::equivalent(edit.before.as_ref(), edit.after.as_ref()) {
                continue;
            }
            changed = true;
            match &edit.after {
                None => {
                    nodes.remove(&edit.id);
                    entities.remove(&edit.id);
                    has_removal = true;
                }
                Some(after_obj) => {
                    if after_obj.kind.contains(ShadowObjectKind::ENTITY) {
                        entities.insert(edit.id.clone());
                    } else {
                        entities.remove(&edit.id);
                    }
                    nodes.insert(edit.id.clone(), Arc::new(after_obj.clone()));
                }
            }
        }

        if !changed {
            return Ok(snapshot.clone());
        }

        // Validate outgoing references (all nodes if any removal occurred, else changed nodes)
        if has_removal {
            for node in nodes.values() {
                for val in node.properties.values() {
                    for reference in val.references() {
                        if !nodes.contains_key(&reference) {
                            return Err(ShadowConflictException::InvalidArgument(format!(
                                "{} contains a dangling reference to {}.",
                                node.id, reference
                            )));
                        }
                    }
                }
            }
        } else {
            for edit in &self.changes {
                if let Some(ref node) = edit.after {
                    for val in node.properties.values() {
                        for reference in val.references() {
                            if !nodes.contains_key(&reference) {
                                return Err(ShadowConflictException::InvalidArgument(format!(
                                    "{} contains a dangling reference to {}.",
                                    node.id, reference
                                )));
                            }
                        }
                    }
                }
            }
        }

        let diagnostics = snapshot
            .diagnostics
            .iter()
            .filter(|d| d.object.as_ref().map_or(true, |id| nodes.contains_key(id)))
            .cloned()
            .collect();

        Ok(ShadowSnapshot {
            document_id: snapshot.document_id.clone(),
            revision: snapshot.revision.checked_add(1).ok_or_else(|| {
                ShadowConflictException::InvalidOperation("Revision overflow.".into())
            })?,
            root: snapshot.root.clone(),
            objects: nodes,
            entity_ids: entities,
            diagnostics,
        })
    }

    pub fn equivalent(left: Option<&ShadowObject>, right: Option<&ShadowObject>) -> bool {
        match (left, right) {
            (None, None) => true,
            (Some(a), Some(b)) => a == b,
            _ => false,
        }
    }
}

/// Store interface supporting full structural [`ShadowGraphPatch`] transactions.
pub trait IShadowGraphStore: IShadowStore {
    fn apply_patch(&self, patch: &ShadowGraphPatch) -> ShadowResult<Arc<ShadowSnapshot>>;
}

/// Mutable transaction builder over persistent snapshot data.
#[derive(Debug, Clone)]
pub struct ShadowGraphEdit {
    baseline: ShadowSnapshot,
    nodes: BTreeMap<ObjectId, Arc<ShadowObject>>,
}

impl ShadowGraphEdit {
    pub fn new(snapshot: ShadowSnapshot) -> Self {
        let nodes = snapshot.objects.clone();
        Self {
            baseline: snapshot,
            nodes,
        }
    }

    pub fn create(
        &mut self,
        type_name: impl Into<String>,
        kind: ShadowObjectKind,
        properties: BTreeMap<String, ShadowValue>,
        identity: Option<ObjectId>,
    ) -> ShadowResult<ObjectId> {
        let type_name = type_name.into();
        if type_name.is_empty() {
            return Err(ShadowConflictException::InvalidArgument(
                "type_name cannot be empty.".into(),
            ));
        }
        let id = identity.unwrap_or_else(|| {
            let seq = NEXT_NODE_ID.fetch_add(1, Ordering::Relaxed);
            ObjectId::new(format!("N:{seq:016x}"))
        });
        if id.is_empty() || self.nodes.contains_key(&id) {
            return Err(ShadowConflictException::InvalidArgument(
                "Object identity is already in use.".into(),
            ));
        }
        let writable_properties: BTreeSet<String> = properties.keys().cloned().collect();
        let obj = ShadowObject {
            id: id.clone(),
            type_name,
            kind,
            properties,
            writable_properties,
        };
        self.nodes.insert(id.clone(), Arc::new(obj));
        Ok(id)
    }

    pub fn set(
        &mut self,
        id: &ObjectId,
        property: impl Into<String>,
        value: ShadowValue,
    ) -> ShadowResult<()> {
        let property = property.into();
        if property.is_empty() {
            return Err(ShadowConflictException::InvalidArgument(
                "property cannot be empty.".into(),
            ));
        }
        let Some(existing) = self.nodes.get(id) else {
            return Err(ShadowConflictException::InvalidArgument(
                "Unknown object.".into(),
            ));
        };
        let mut updated = (**existing).clone();
        updated.properties.insert(property, value);
        self.nodes.insert(id.clone(), Arc::new(updated));
        Ok(())
    }

    pub fn remove(&mut self, id: &ObjectId) -> ShadowResult<()> {
        if id == &self.baseline.root {
            return Err(ShadowConflictException::InvalidArgument(
                "Cannot remove the document root.".into(),
            ));
        }
        self.nodes.remove(id);
        Ok(())
    }

    pub fn build(&self) -> ShadowGraphPatch {
        let mut changes = Vec::new();
        for (id, before_node) in &self.baseline.objects {
            match self.nodes.get(id) {
                None => changes.push(ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**before_node).clone()),
                    after: None,
                }),
                Some(after_node)
                    if !Arc::ptr_eq(before_node, after_node) && before_node != after_node =>
                {
                    changes.push(ShadowObjectChange {
                        id: id.clone(),
                        before: Some((**before_node).clone()),
                        after: Some((**after_node).clone()),
                    });
                }
                _ => {}
            }
        }
        for (id, after_node) in &self.nodes {
            if !self.baseline.objects.contains_key(id) {
                changes.push(ShadowObjectChange {
                    id: id.clone(),
                    before: None,
                    after: Some((**after_node).clone()),
                });
            }
        }
        ShadowGraphPatch::new(
            self.baseline.document_id.clone(),
            self.baseline.revision,
            changes,
        )
    }
}
