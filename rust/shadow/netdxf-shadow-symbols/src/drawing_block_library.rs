#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{DiagnosticSeverity, ObjectId, Vec3};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingBlockEntry {
    pub name: String,
    pub entity_count: usize,
}

pub struct DrawingBlockLibrary;

impl DrawingBlockLibrary {
    pub fn summarize(names: &[String]) -> Vec<DrawingBlockEntry> {
        names
            .iter()
            .map(|n| DrawingBlockEntry {
                name: n.clone(),
                entity_count: 0,
            })
            .collect()
    }
}

