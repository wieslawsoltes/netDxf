#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow_studio_core::StudioDocumentHub;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
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

