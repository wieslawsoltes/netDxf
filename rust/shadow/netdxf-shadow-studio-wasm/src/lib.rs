//! FerroUI WebAssembly studio runtime bridge (`netDxf.Shadow.Studio.WebAssembly`).

#![forbid(unsafe_code)]

use netdxf_shadow_studio_core::StudioDocumentHub;
use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BrowserRequest {
    pub id: String,
    pub route: String,
    pub payload: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BrowserReply {
    pub id: String,
    pub ok: bool,
    pub payload: Value,
    pub error: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BrowserNotice {
    pub topic: String,
    pub payload: Value,
}

#[derive(Default)]
pub struct BrowserRuntime {
    pub hub: StudioDocumentHub,
}

impl BrowserRuntime {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn dispatch(&mut self, request: BrowserRequest) -> BrowserReply {
        BrowserReply {
            id: request.id,
            ok: true,
            payload: serde_json::json!({ "route": request.route }),
            error: None,
        }
    }
}
