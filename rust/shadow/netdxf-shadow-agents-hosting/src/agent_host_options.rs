#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentHostOptions {
    pub bind_address: String,
    pub port: u16,
    pub allowed_origins: Vec<String>,
    pub callback_uri: Option<String>,
    pub state_file: Option<String>,
    pub allow_processes: bool,
    pub acp_executable: Option<String>,
    pub acp_arguments: Vec<String>,
    pub codex_executable: Option<String>,
    pub maximum_workspace_files: usize,
    pub maximum_workspace_bytes: usize,
    pub validate_token: bool,
    pub workspace: Option<String>,
}

impl Default for AgentHostOptions {
    fn default() -> Self {
        Self {
            bind_address: "127.0.0.1".into(),
            port: 8787,
            allowed_origins: vec!["http://localhost".into()],
            callback_uri: None,
            state_file: None,
            allow_processes: false,
            acp_executable: None,
            acp_arguments: Vec::new(),
            codex_executable: None,
            maximum_workspace_files: 10_000,
            maximum_workspace_bytes: 128 * 1024 * 1024,
            validate_token: true,
            workspace: None,
        }
    }
}
