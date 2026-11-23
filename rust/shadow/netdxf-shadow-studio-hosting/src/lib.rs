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
