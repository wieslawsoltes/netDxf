#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentCodeRequest {
    pub language: String,
    pub code: String,
    pub timeout_ms: u64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentCodeChange {
    pub path: String,
    pub summary: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentCodeResult {
    pub exit_code: i32,
    pub result: Option<String>,
    pub stdout: String,
    pub standard_output: String,
    pub stderr: String,
    pub standard_error: String,
    pub elapsed_milliseconds: u64,
    pub timed_out: bool,
    pub truncated: bool,
    pub changes: Vec<AgentCodeChange>,
}

pub trait IAgentCodeRunner: Send + Sync {
    fn run(&self, request: &AgentCodeRequest) -> AgentCodeResult;
}

pub struct AgentCodeTools;

impl AgentCodeTools {
    pub fn supported_languages() -> &'static [&'static str] {
        &["rust", "python", "javascript"]
    }
}

