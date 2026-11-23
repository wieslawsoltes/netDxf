//! Studio agent controller and UI relay (`netDxf.Shadow.Studio.Agents`).

#![forbid(unsafe_code)]

use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct StudioAgentConfiguration {
    pub provider: String,
    pub model: String,
    pub max_turns: usize,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Request {
    pub prompt: String,
    pub context: Value,
}

pub struct StudioAgentController {
    pub config: StudioAgentConfiguration,
}

pub struct StudioAgentUiRelay;

impl StudioAgentUiRelay {
    pub fn format_prompt(req: &Request) -> String {
        req.prompt.trim().to_string()
    }
}
