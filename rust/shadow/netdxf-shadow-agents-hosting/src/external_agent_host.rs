#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

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

pub struct ExternalAgentHost;

impl ExternalAgentHost {
    pub fn ok_reply(id: impl Into<String>, result: Value) -> ExternalAgentReply {
        ExternalAgentReply {
            id: id.into(),
            ok: true,
            result,
        }
    }
}

