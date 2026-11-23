#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioDocumentHubHttp;

impl StudioDocumentHubHttp {
    pub fn routes() -> &'static [&'static str] {
        &["/api/documents", "/api/documents/new", "/api/documents/close"]
    }

    pub fn load(document_id: &str) -> Value {
        serde_json::json!({ "documentId": document_id, "loaded": true })
    }

    pub fn save(document_id: &str) -> Value {
        serde_json::json!({ "documentId": document_id, "saved": true })
    }

    pub fn close(document_id: &str) -> Value {
        serde_json::json!({ "documentId": document_id, "closed": true })
    }
}

