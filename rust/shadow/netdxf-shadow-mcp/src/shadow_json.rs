#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use std::sync::Arc;

pub struct ShadowJson;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Location {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

impl ShadowJson {
    pub fn snapshot_summary(snapshot: &ShadowSnapshot) -> Value {
        serde_json::json!({
            "documentId": snapshot.document_id,
            "revision": snapshot.revision,
            "objectCount": snapshot.objects.len(),
            "entityCount": snapshot.entity_ids.len(),
        })
    }
}

