#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Request {
    pub prompt: String,
    pub context: Value,
}

pub struct StudioAgentUiRelay;

impl StudioAgentUiRelay {
    pub fn format_prompt(req: &Request) -> String {
        req.prompt.trim().to_string()
    }
}

