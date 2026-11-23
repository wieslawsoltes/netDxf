#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow_studio_core::StudioDocumentHub;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BrowserRequest {
    pub id: String,
    pub route: String,
    pub method: String,
    pub path: String,
    pub headers: std::collections::BTreeMap<String, String>,
    pub body: Option<String>,
    pub payload: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BrowserReply {
    pub id: String,
    pub ok: bool,
    pub status: u16,
    pub content_type: String,
    pub payload: Value,
    pub value: Value,
    pub bytes: Vec<u8>,
    pub error: Option<String>,
}

impl BrowserReply {
    pub fn json(id: impl Into<String>, value: Value) -> Self {
        Self {
            id: id.into(),
            ok: true,
            status: 200,
            content_type: "application/json".into(),
            payload: value.clone(),
            value,
            bytes: Vec::new(),
            error: None,
        }
    }

    pub fn binary(id: impl Into<String>, content_type: impl Into<String>, bytes: Vec<u8>) -> Self {
        Self {
            id: id.into(),
            ok: true,
            status: 200,
            content_type: content_type.into(),
            payload: Value::Null,
            value: Value::Null,
            bytes,
            error: None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BrowserNotice {
    pub topic: String,
    pub event_id: u64,
    pub payload: Value,
    pub data: Value,
}

