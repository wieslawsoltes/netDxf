#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct Request {
    pub id: String,
    pub action: String,
    pub expected_document_id: Option<String>,
    pub arguments: Value,
    pub prompt: String,
    pub context: Value,
}

#[derive(Debug, Clone, Default)]
pub struct StudioAgentUiRelay {
    pub pending: Vec<Request>,
}

impl StudioAgentUiRelay {
    pub fn format_prompt(req: &Request) -> String {
        req.prompt.trim().to_string()
    }

    pub fn invoke(&mut self, req: Request) -> Value {
        self.pending.push(req.clone());
        serde_json::json!({ "id": req.id, "action": req.action, "queued": true })
    }

    pub fn poll(&self) -> Vec<Request> {
        self.pending.clone()
    }

    pub fn reply(&mut self, id: &str, payload: Value) -> Value {
        self.pending.retain(|r| r.id != id);
        serde_json::json!({ "id": id, "reply": payload })
    }
}

