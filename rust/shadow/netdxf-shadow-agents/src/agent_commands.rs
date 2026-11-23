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
pub struct AgentCommand {
    pub program: String,
    pub args: Vec<String>,
    pub working_directory: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentCommandResult {
    pub exit_code: i32,
    pub stdout: String,
    pub stderr: String,
}

pub trait IAgentCommandRunner: Send + Sync {
    fn run(&self, command: &AgentCommand) -> AgentCommandResult;
}

#[derive(Debug, Default)]
pub struct LocalAgentCommandRunner;

pub struct AgentCommandTools;

impl IAgentCommandRunner for LocalAgentCommandRunner {
    fn run(&self, command: &AgentCommand) -> AgentCommandResult {
        AgentCommandResult {
            exit_code: 0,
            stdout: format!("dry-run: {} {:?}", command.program, command.args),
            stderr: String::new(),
        }
    }
}

impl AgentCommandTools {
    pub fn is_allowed(mode: AgentPermissionMode) -> bool {
        matches!(mode, AgentPermissionMode::FullAccess)
    }
}

