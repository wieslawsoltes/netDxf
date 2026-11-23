#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioHttp;

impl StudioHttp {
    pub fn error(message: impl Into<String>) -> Envelope {
        Envelope {
            ok: false,
            payload: Value::Null,
            error: Some(message.into()),
        }
    }

    pub fn identity(document_id: &str) -> Value {
        serde_json::json!({ "documentId": document_id })
    }

    pub fn map_studio_workspaces() -> &'static [&'static str] {
        &["/api/studio/workspace", "/api/studio/fork", "/api/studio/merge"]
    }

    pub fn presentation(payload: Value) -> Envelope {
        Envelope {
            ok: true,
            payload,
            error: None,
        }
    }
}

