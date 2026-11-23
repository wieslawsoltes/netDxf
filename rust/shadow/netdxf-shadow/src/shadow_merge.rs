use crate::shadow_graph_patch::{IShadowGraphStore, ShadowGraphPatch, ShadowObjectChange};
use crate::shadow_snapshot::{ShadowConflictException, ShadowResult, ShadowSnapshot};
use crate::shadow_value::{ObjectId, ShadowObject, ShadowValue, Structure};
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::sync::Arc;

/// Conflict resolution policy for three-way [`ShadowMerge`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ShadowMergeChoice {
    #[default]
    Unresolved,
    Target,
    Incoming,
}

/// Classification of a three-way merge conflict.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum ShadowMergeConflictKind {
    Value,
    ConcurrentAddition,
    DeleteModify,
    Metadata,
    DanglingReference,
}

/// Describes a single conflict encountered while planning a three-way merge.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowMergeConflict {
    pub object: ObjectId,
    pub path: Vec<String>,
    pub kind: ShadowMergeConflictKind,
    pub resolution: ShadowMergeChoice,
    pub base_value: Option<ShadowValue>,
    pub target_value: Option<ShadowValue>,
    pub incoming_value: Option<ShadowValue>,
    pub referenced_object: Option<ObjectId>,
}

/// Resource budgets and conflict policy for [`ShadowMerge::plan`].
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowMergeOptions {
    pub conflict_choice: ShadowMergeChoice,
    pub maximum_objects: usize,
    pub maximum_work: i64,
    pub maximum_value_depth: usize,
    pub maximum_conflicts: usize,
}

impl Default for ShadowMergeOptions {
    fn default() -> Self {
        Self {
            conflict_choice: ShadowMergeChoice::Unresolved,
            maximum_objects: 1_000_000,
            maximum_work: 50_000_000,
            maximum_value_depth: 64,
            maximum_conflicts: 10_000,
        }
    }
}

/// Pure planning result of [`ShadowMerge::plan`].
#[derive(Debug, Clone)]
pub struct ShadowMergePlan {
    pub ancestor: Arc<ShadowSnapshot>,
    pub target: Arc<ShadowSnapshot>,
    pub incoming: Arc<ShadowSnapshot>,
    pub conflicts: Vec<ShadowMergeConflict>,
    pub patch: Option<ShadowGraphPatch>,
    pub preview: Option<ShadowSnapshot>,
    pub work: i64,
}

impl ShadowMergePlan {
    pub fn can_apply(&self) -> bool {
        self.patch.is_some()
    }

    pub fn apply_to(&self, store: &dyn IShadowGraphStore) -> ShadowResult<Arc<ShadowSnapshot>> {
        let Some(ref patch) = self.patch else {
            return Err(ShadowConflictException::Conflict(
                "Resolve merge conflicts before applying the plan.".into(),
            ));
        };
        let current = store.current();
        if current.document_id != self.target.document_id
            || current.revision != self.target.revision
        {
            return Err(ShadowConflictException::Conflict(
                "Merge target changed or belongs to another branch. Replan the merge.".into(),
            ));
        }
        store.apply_patch(patch)
    }
}

/// Renderer/source-independent three-way object-graph merge.
pub struct ShadowMerge;

impl ShadowMerge {
    pub fn plan(
        ancestor: Arc<ShadowSnapshot>,
        target: Arc<ShadowSnapshot>,
        incoming: Arc<ShadowSnapshot>,
        options: ShadowMergeOptions,
    ) -> ShadowResult<ShadowMergePlan> {
        if options.maximum_objects < 1
            || options.maximum_work < 1
            || !(1..=256).contains(&options.maximum_value_depth)
            || options.maximum_conflicts < 1
        {
            return Err(ShadowConflictException::InvalidArgument(
                "Invalid ShadowMergeOptions budgets.".into(),
            ));
        }
        if ancestor.document_id != target.document_id
            || ancestor.document_id != incoming.document_id
            || ancestor.root != target.root
            || ancestor.root != incoming.root
        {
            return Err(ShadowConflictException::InvalidArgument(
                "Merging requires the same document and root identity.".into(),
            ));
        }
        if ancestor.revision < 0
            || target.revision < ancestor.revision
            || incoming.revision < ancestor.revision
        {
            return Err(ShadowConflictException::InvalidArgument(
                "Branch revisions cannot precede the supplied ancestor.".into(),
            ));
        }

        let mut ids = BTreeSet::new();
        ids.extend(ancestor.objects.keys().cloned());
        ids.extend(target.objects.keys().cloned());
        ids.extend(incoming.objects.keys().cloned());
        if ids.len() > options.maximum_objects {
            return Err(ShadowConflictException::InvalidOperation(
                "Merge object-union budget exceeded.".into(),
            ));
        }

        let mut planner = Planner {
            options,
            work: 0,
            conflicts: Vec::new(),
        };

        let mut edits = Vec::new();
        let mut nodes = target.objects.clone();

        for id in ids {
            planner.tick()?;
            let b = ancestor.objects.get(&id).map(|a| a.as_ref());
            let t = target.objects.get(&id).map(|a| a.as_ref());
            let i = incoming.objects.get(&id).map(|a| a.as_ref());
            if let Some(b_obj) = b {
                if t.is_some_and(|x| x.type_name != b_obj.type_name)
                    || i.is_some_and(|x| x.type_name != b_obj.type_name)
                {
                    return Err(ShadowConflictException::InvalidArgument(format!(
                        "Runtime type changed for existing identity {id}."
                    )));
                }
            }
            let merged = planner.merge_object(&id, b, t, i)?;
            if merged.as_ref() == t {
                continue;
            }
            edits.push(ShadowObjectChange {
                id: id.clone(),
                before: t.cloned(),
                after: merged.clone(),
            });
            match merged {
                None => {
                    nodes.remove(&id);
                }
                Some(m) => {
                    nodes.insert(id, Arc::new(m));
                }
            }
        }

        if planner
            .conflicts
            .iter()
            .any(|c| c.resolution == ShadowMergeChoice::Unresolved)
        {
            return Ok(ShadowMergePlan {
                ancestor,
                target,
                incoming,
                conflicts: planner.conflicts,
                patch: None,
                preview: None,
                work: planner.work,
            });
        }

        // Check for dangling references across resulting nodes
        for node in nodes.values() {
            for (prop, val) in &node.properties {
                for r in val.references() {
                    if !nodes.contains_key(&r) {
                        planner.record_conflict(ShadowMergeConflict {
                            object: node.id.clone(),
                            path: vec![prop.clone()],
                            kind: ShadowMergeConflictKind::DanglingReference,
                            resolution: ShadowMergeChoice::Unresolved,
                            base_value: None,
                            target_value: None,
                            incoming_value: None,
                            referenced_object: Some(r),
                        })?;
                    }
                }
            }
        }

        if planner
            .conflicts
            .iter()
            .any(|c| c.resolution == ShadowMergeChoice::Unresolved)
        {
            return Ok(ShadowMergePlan {
                ancestor,
                target,
                incoming,
                conflicts: planner.conflicts,
                patch: None,
                preview: None,
                work: planner.work,
            });
        }

        let patch = ShadowGraphPatch::new(target.document_id.clone(), target.revision, edits);
        let preview = patch.apply(&target)?;
        Ok(ShadowMergePlan {
            ancestor,
            target,
            incoming,
            conflicts: planner.conflicts,
            patch: Some(patch),
            preview: Some(preview),
            work: planner.work,
        })
    }
}

struct Planner {
    options: ShadowMergeOptions,
    work: i64,
    conflicts: Vec<ShadowMergeConflict>,
}

impl Planner {
    fn tick(&mut self) -> ShadowResult<()> {
        self.work += 1;
        if self.work > self.options.maximum_work {
            return Err(ShadowConflictException::InvalidOperation(
                "Merge work budget exceeded.".into(),
            ));
        }
        Ok(())
    }

    fn record_conflict(&mut self, conflict: ShadowMergeConflict) -> ShadowResult<()> {
        if self.conflicts.len() >= self.options.maximum_conflicts {
            return Err(ShadowConflictException::InvalidOperation(
                "Merge conflict budget exceeded.".into(),
            ));
        }
        self.conflicts.push(conflict);
        Ok(())
    }

    fn merge_object(
        &mut self,
        id: &ObjectId,
        b: Option<&ShadowObject>,
        t: Option<&ShadowObject>,
        i: Option<&ShadowObject>,
    ) -> ShadowResult<Option<ShadowObject>> {
        if t == i || b == i {
            return Ok(t.cloned());
        }
        if b == t {
            return Ok(i.cloned());
        }
        let (Some(b_obj), Some(t_obj), Some(i_obj)) = (b, t, i) else {
            let kind = if b.is_none() {
                ShadowMergeConflictKind::ConcurrentAddition
            } else {
                ShadowMergeConflictKind::DeleteModify
            };
            self.record_conflict(ShadowMergeConflict {
                object: id.clone(),
                path: Vec::new(),
                kind,
                resolution: self.options.conflict_choice,
                base_value: None,
                target_value: None,
                incoming_value: None,
                referenced_object: None,
            })?;
            return Ok(if self.options.conflict_choice == ShadowMergeChoice::Incoming {
                i.cloned()
            } else {
                t.cloned()
            });
        };

        let same_meta = |x: &ShadowObject, y: &ShadowObject| {
            x.kind == y.kind && x.writable_properties == y.writable_properties
        };
        let mut metadata = t_obj;
        if !same_meta(t_obj, i_obj) && !same_meta(b_obj, i_obj) {
            if same_meta(b_obj, t_obj) {
                metadata = i_obj;
            } else {
                self.record_conflict(ShadowMergeConflict {
                    object: id.clone(),
                    path: Vec::new(),
                    kind: ShadowMergeConflictKind::Metadata,
                    resolution: self.options.conflict_choice,
                    base_value: None,
                    target_value: None,
                    incoming_value: None,
                    referenced_object: None,
                })?;
                if self.options.conflict_choice == ShadowMergeChoice::Incoming {
                    metadata = i_obj;
                }
            }
        }

        let properties = self.merge_fields(
            id,
            &[],
            &b_obj.properties,
            &t_obj.properties,
            &i_obj.properties,
            0,
        )?;

        Ok(Some(ShadowObject {
            id: t_obj.id.clone(),
            type_name: t_obj.type_name.clone(),
            kind: metadata.kind,
            properties,
            writable_properties: metadata.writable_properties.clone(),
        }))
    }

    fn merge_fields(
        &mut self,
        id: &ObjectId,
        path: &[String],
        b: &BTreeMap<String, ShadowValue>,
        t: &BTreeMap<String, ShadowValue>,
        i: &BTreeMap<String, ShadowValue>,
        depth: usize,
    ) -> ShadowResult<BTreeMap<String, ShadowValue>> {
        if depth > self.options.maximum_value_depth {
            return Err(ShadowConflictException::InvalidOperation(
                "Merge value depth budget exceeded.".into(),
            ));
        }
        let mut keys = BTreeSet::new();
        keys.extend(b.keys().cloned());
        keys.extend(t.keys().cloned());
        keys.extend(i.keys().cloned());

        let mut out = BTreeMap::new();
        for key in keys {
            self.tick()?;
            let bv = b.get(&key);
            let tv = t.get(&key);
            let iv = i.get(&key);
            let chosen = if tv == iv || bv == iv {
                tv.cloned()
            } else if bv == tv {
                iv.cloned()
            } else if let (
                Some(ShadowValue::Structure(bs)),
                Some(ShadowValue::Structure(ts)),
                Some(ShadowValue::Structure(is_)),
            ) = (bv, tv, iv)
            {
                if bs.type_name == ts.type_name && bs.type_name == is_.type_name {
                    let mut next_path = path.to_vec();
                    next_path.push(key.clone());
                    let merged_fields = self.merge_fields(
                        id,
                        &next_path,
                        &bs.fields,
                        &ts.fields,
                        &is_.fields,
                        depth + 1,
                    )?;
                    Some(ShadowValue::Structure(Structure {
                        type_name: ts.type_name.clone(),
                        fields: merged_fields,
                    }))
                } else {
                    let mut next_path = path.to_vec();
                    next_path.push(key.clone());
                    self.record_conflict(ShadowMergeConflict {
                        object: id.clone(),
                        path: next_path,
                        kind: ShadowMergeConflictKind::Value,
                        resolution: self.options.conflict_choice,
                        base_value: bv.cloned(),
                        target_value: tv.cloned(),
                        incoming_value: iv.cloned(),
                        referenced_object: None,
                    })?;
                    if self.options.conflict_choice == ShadowMergeChoice::Incoming {
                        iv.cloned()
                    } else {
                        tv.cloned()
                    }
                }
            } else {
                let mut next_path = path.to_vec();
                next_path.push(key.clone());
                self.record_conflict(ShadowMergeConflict {
                    object: id.clone(),
                    path: next_path,
                    kind: ShadowMergeConflictKind::Value,
                    resolution: self.options.conflict_choice,
                    base_value: bv.cloned(),
                    target_value: tv.cloned(),
                    incoming_value: iv.cloned(),
                    referenced_object: None,
                })?;
                if self.options.conflict_choice == ShadowMergeChoice::Incoming {
                    iv.cloned()
                } else {
                    tv.cloned()
                }
            };
            if let Some(val) = chosen {
                out.insert(key, val);
            }
        }
        Ok(out)
    }
}
