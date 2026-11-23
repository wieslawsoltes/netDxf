//! External agent host and studio relay (`netDxf.Shadow.Agents.Hosting`).

#![forbid(unsafe_code)]

use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentHostOptions {
    pub bind_address: String,
    pub port: u16,
}

impl Default for AgentHostOptions {
    fn default() -> Self {
        Self {
            bind_address: "127.0.0.1".into(),
            port: 8787,
        }
    }
}

pub struct AgentHostApplication {
    pub options: AgentHostOptions,
}

pub struct BrowserStudioRelay;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptHostStatus {
    pub signed_in: bool,
    pub account_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChatGptHostLease {
    pub lease_id: String,
    pub expires_at_unix: u64,
}

pub struct ChatGptHostSession;
pub struct CompanionWorkspace;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ExternalAgentEvent {
    pub seq: u64,
    pub kind: String,
    pub data: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ExternalAgentCall {
    pub id: String,
    pub method: String,
    pub params: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ExternalAgentReply {
    pub id: String,
    pub ok: bool,
    pub result: Value,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ExternalAgentHostSettings {
    pub provider: String,
    pub working_directory: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct StartRequest {
    pub session_id: String,
    pub prompt: String,
}

pub struct ExternalAgentHost;
pub struct ExternalAgentHttp;
