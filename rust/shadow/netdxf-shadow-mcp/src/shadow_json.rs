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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct Location {
    pub x: f64,
    pub y: f64,
    pub z: f64,
    pub object: Option<ObjectId>,
    pub property: Option<String>,
    pub nested: Option<String>,
    pub value: Option<Value>,
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

    pub fn describe(value: &Value) -> &'static str {
        match value {
            Value::Null => "null",
            Value::Bool(_) => "boolean",
            Value::Number(_) => "number",
            Value::String(_) => "string",
            Value::Array(_) => "array",
            Value::Object(_) => "object",
        }
    }

    pub fn escape(segment: &str) -> String {
        segment.replace('~', "~0").replace('/', "~1")
    }

    pub fn from_input(input: &str) -> Result<Value, String> {
        serde_json::from_str(input).map_err(|e| e.to_string())
    }

    pub fn locate<'a>(root: &'a Value, pointer: &str) -> Option<&'a Value> {
        root.pointer(pointer)
    }

    pub fn location(x: f64, y: f64, z: f64) -> Location {
        Location {
            x,
            y,
            z,
            ..Default::default()
        }
    }

    pub fn replace(root: &mut Value, pointer: &str, replacement: Value) -> bool {
        if let Some(slot) = root.pointer_mut(pointer) {
            *slot = replacement;
            true
        } else {
            false
        }
    }
}

