#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct StartRequest {
    pub session_id: String,
    pub prompt: String,
    pub profile: Option<String>,
}

pub struct ExternalAgentHttp;

impl ExternalAgentHttp {
    pub fn health_payload() -> Value {
        serde_json::json!({ "status": "ok" })
    }

    pub fn map_external_agents() -> &'static [&'static str] {
        &["/api/external-agents/start", "/api/external-agents/stop", "/api/external-agents/call"]
    }
}

