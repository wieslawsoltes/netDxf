use crate::shadow_snapshot::ShadowSnapshot;
use crate::shadow_value::{ObjectId, ShadowObject};
use std::collections::BTreeMap;
use std::sync::Arc;

/// Per-operation read observer. Observers must not mutate the supplied immutable nodes.
pub trait IShadowReadObserver {
    fn read(&mut self, id: &ObjectId, node: Option<&Arc<ShadowObject>>);
}

/// Recorded dependency on a single object identity and its observed immutable node.
#[derive(Debug, Clone)]
pub struct ShadowDependency {
    pub id: ObjectId,
    pub node: Option<Arc<ShadowObject>>,
}

impl ShadowDependency {
    pub fn is_current(&self, snapshot: &ShadowSnapshot) -> bool {
        match (&self.node, snapshot.objects.get(&self.id)) {
            (Some(a), Some(b)) => Arc::ptr_eq(a, b) || a == b,
            (None, None) => true,
            _ => false,
        }
    }
}

/// Tracks only the nodes actually dereferenced by a consumer.
#[derive(Debug, Default)]
pub struct ShadowReadSet {
    first: Option<ShadowDependency>,
    others: Option<BTreeMap<ObjectId, Option<Arc<ShadowObject>>>>,
}

impl ShadowReadSet {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn freeze(self) -> Vec<ShadowDependency> {
        let Some(first) = self.first else {
            return Vec::new();
        };
        let Some(others) = self.others else {
            return vec![first];
        };
        let mut out = Vec::with_capacity(others.len() + 1);
        out.push(first);
        for (id, node) in others {
            out.push(ShadowDependency { id, node });
        }
        out
    }
}

impl IShadowReadObserver for ShadowReadSet {
    fn read(&mut self, id: &ObjectId, node: Option<&Arc<ShadowObject>>) {
        match &mut self.first {
            None => {
                self.first = Some(ShadowDependency {
                    id: id.clone(),
                    node: node.cloned(),
                });
            }
            Some(f) if &f.id == id => {
                f.node = node.cloned();
            }
            Some(_) => {
                self.others
                    .get_or_insert_with(BTreeMap::new)
                    .insert(id.clone(), node.cloned());
            }
        }
    }
}
