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

#[derive(Debug, Clone, Default)]
pub struct ExternalAgentHost {
    pub settings: ExternalAgentHostSettings,
    pub running: bool,
    pub events: Vec<ExternalAgentEvent>,
}

impl ExternalAgentHost {
    pub fn new(settings: ExternalAgentHostSettings) -> Self {
        Self {
            settings,
            running: false,
            events: Vec::new(),
        }
    }

    pub fn profiles() -> Vec<&'static str> {
        vec!["codex", "acp", "claude", "gemini"]
    }

    pub fn start(&mut self) {
        self.running = true;
    }

    pub fn stop(&mut self) {
        self.running = false;
    }

    pub fn call(&mut self, request: ExternalAgentCall) -> ExternalAgentReply {
        let seq = (self.events.len() as u64) + 1;
        self.events.push(ExternalAgentEvent {
            seq,
            kind: request.method.clone(),
            data: request.params.clone(),
        });
        Self::reply(request.id, self.running, serde_json::json!({ "method": request.method }))
    }

    pub fn poll(&self, after_seq: u64) -> Vec<ExternalAgentEvent> {
        self.events
            .iter()
            .filter(|e| e.seq > after_seq)
            .cloned()
            .collect()
    }

    pub fn reply(id: impl Into<String>, ok: bool, result: Value) -> ExternalAgentReply {
        ExternalAgentReply {
            id: id.into(),
            ok,
            result,
        }
    }

    pub fn ok_reply(id: impl Into<String>, result: Value) -> ExternalAgentReply {
        Self::reply(id, true, result)
    }
}

