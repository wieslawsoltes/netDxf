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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentCommand {
    pub program: String,
    pub executable: String,
    pub args: Vec<String>,
    pub arguments: Vec<String>,
    pub timeout_seconds: u64,
    pub working_directory: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentCommandResult {
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
    pub standard_output: String,
    pub standard_error: String,
    pub elapsed_milliseconds: u64,
    pub timed_out: bool,
    pub truncated: bool,
}

pub trait IAgentCommandRunner: Send + Sync {
    fn run(&self, command: &AgentCommand) -> AgentCommandResult;

    fn execute_async(&self, command: &AgentCommand) -> AgentCommandResult {
        self.run(command)
    }
}

#[derive(Debug, Default)]
pub struct LocalAgentCommandRunner;

pub struct AgentCommandTools;

impl IAgentCommandRunner for LocalAgentCommandRunner {
    fn run(&self, command: &AgentCommand) -> AgentCommandResult {
        let out = format!("dry-run: {} {:?}", command.program, command.args);
        AgentCommandResult {
            exit_code: 0,
            stdout: out.clone(),
            stderr: String::new(),
            standard_output: out,
            standard_error: String::new(),
            elapsed_milliseconds: 0,
            timed_out: false,
            truncated: false,
        }
    }
}

impl AgentCommandTools {
    pub fn is_allowed(mode: AgentPermissionMode) -> bool {
        matches!(mode, AgentPermissionMode::FullAccess)
    }

    pub fn register() -> &'static [&'static str] {
        &["command.execute"]
    }
}

