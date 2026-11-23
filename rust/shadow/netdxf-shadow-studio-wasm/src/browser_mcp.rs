#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow_studio_core::StudioDocumentHub;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Default)]
pub struct BrowserRuntime {
    pub hub: StudioDocumentHub,
}

impl BrowserRuntime {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn dispatch(&mut self, request: BrowserRequest) -> BrowserReply {
        match request.route.as_str() {
            "ping" => BrowserReply::json(request.id, serde_json::json!({ "pong": true })),
            _ => BrowserReply::json(request.id, serde_json::json!({ "route": request.route })),
        }
    }
}

