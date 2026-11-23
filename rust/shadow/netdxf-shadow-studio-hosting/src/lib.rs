//! Studio HTTP/RPC host adapters (`netDxf.Shadow.Studio.Hosting`).

#![forbid(unsafe_code)]

use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Envelope {
    pub ok: bool,
    pub payload: Value,
    pub error: Option<String>,
}

pub struct StudioAgentHttp;
pub struct StudioCadHttp;
pub struct StudioDocumentHubHttp;
pub struct StudioHttp;
pub struct StudioLegacyHttp;

impl StudioAgentHttp {
    pub fn ok(payload: Value) -> Envelope {
        Envelope {
            ok: true,
            payload,
            error: None,
        }
    }
}

impl StudioCadHttp {
    pub fn routes() -> &'static [&'static str] {
        &["/api/cad/snapshot", "/api/cad/render", "/api/cad/pick"]
    }
}

impl StudioDocumentHubHttp {
    pub fn routes() -> &'static [&'static str] {
        &["/api/documents", "/api/documents/new", "/api/documents/close"]
    }
}

impl StudioHttp {
    pub fn error(message: impl Into<String>) -> Envelope {
        Envelope {
            ok: false,
            payload: Value::Null,
            error: Some(message.into()),
        }
    }
}

impl StudioLegacyHttp {
    pub fn is_deprecated() -> bool {
        true
    }
}
