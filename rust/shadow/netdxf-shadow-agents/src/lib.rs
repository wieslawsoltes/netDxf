//! Autonomous CAD agent sessions, workspaces, tools, and providers (`netDxf.Shadow.Agents`).

#![forbid(unsafe_code)]

use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
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
    pub stdout: String,
    pub stderr: String,
    pub changes: Vec<AgentCodeChange>,
}

pub trait IAgentCodeRunner: Send + Sync {
    fn run(&self, request: &AgentCodeRequest) -> AgentCodeResult;
}

pub struct AgentCodeTools;

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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum AgentPermissionMode {
    #[default]
    ReadOnly,
    WorkspaceWrite,
    FullAccess,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum AgentRunMode {
    #[default]
    Interactive,
    Autonomous,
    PlanOnly,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentOptions {
    pub permission_mode: AgentPermissionMode,
    pub run_mode: AgentRunMode,
    pub max_turns: usize,
}

impl Default for AgentOptions {
    fn default() -> Self {
        Self {
            permission_mode: AgentPermissionMode::ReadOnly,
            run_mode: AgentRunMode::Interactive,
            max_turns: 32,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AgentEvent {
    pub seq: u64,
    pub kind: String,
    pub payload: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AgentPollResult {
    pub events: Vec<AgentEvent>,
    pub next_seq: u64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentUsage {
    pub input_tokens: u64,
    pub output_tokens: u64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentPinnedContext {
    pub key: String,
    pub content: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentApproval {
    pub id: String,
    pub description: String,
    pub approved: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentPlanStep {
    pub title: String,
    pub status: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentRunState {
    pub running: bool,
    pub turn: usize,
    pub usage: AgentUsage,
    pub plan: Vec<AgentPlanStep>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentContentRecord {
    pub kind: String,
    pub text: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentMessageRecord {
    pub role: String,
    pub content: Vec<AgentContentRecord>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AgentCallReceipt {
    pub tool: String,
    pub arguments: Value,
    pub result: Value,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentCheckpoint {
    pub id: String,
    pub turn: usize,
    pub messages: Vec<AgentMessageRecord>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentPrompt {
    pub text: String,
    pub pinned: Vec<AgentPinnedContext>,
}

#[derive(Debug, Default)]
pub struct AgentSession {
    pub options: AgentOptions,
    pub state: AgentRunState,
    pub messages: Vec<AgentMessageRecord>,
}

impl AgentSession {
    pub fn new(options: AgentOptions) -> Self {
        Self {
            options,
            state: AgentRunState::default(),
            messages: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFile {
    pub path: String,
    pub content: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFileInfo {
    pub path: String,
    pub size: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFileSlice {
    pub path: String,
    pub start_line: usize,
    pub lines: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentSearchMatch {
    pub path: String,
    pub line: usize,
    pub text: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFileChange {
    pub path: String,
    pub kind: String,
}

pub trait IAgentWorkspace: Send + Sync {
    fn list_files(&self) -> Vec<AgentFileInfo>;
    fn read_file(&self, path: &str) -> Option<String>;
    fn write_file(&mut self, path: &str, content: String);
}

pub trait IAgentTransactionalWorkspace: IAgentWorkspace {
    fn changes(&self) -> Vec<AgentFileChange>;
    fn commit(&mut self);
    fn rollback(&mut self);
}

#[derive(Debug, Default)]
pub struct MemoryAgentWorkspace {
    files: BTreeMap<String, String>,
    staged_changes: Vec<AgentFileChange>,
}

impl IAgentWorkspace for MemoryAgentWorkspace {
    fn list_files(&self) -> Vec<AgentFileInfo> {
        self.files
            .iter()
            .map(|(p, c)| AgentFileInfo {
                path: p.clone(),
                size: c.len(),
            })
            .collect()
    }

    fn read_file(&self, path: &str) -> Option<String> {
        self.files.get(path).cloned()
    }

    fn write_file(&mut self, path: &str, content: String) {
        self.files.insert(path.to_string(), content);
        self.staged_changes.push(AgentFileChange {
            path: path.to_string(),
            kind: "write".into(),
        });
    }
}

impl IAgentTransactionalWorkspace for MemoryAgentWorkspace {
    fn changes(&self) -> Vec<AgentFileChange> {
        self.staged_changes.clone()
    }

    fn commit(&mut self) {
        self.staged_changes.clear();
    }

    fn rollback(&mut self) {
        self.staged_changes.clear();
    }
}

#[derive(Debug, Clone)]
pub struct DirectoryAgentWorkspace {
    pub root: String,
}

pub struct AgentWorkspaceTools;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptSignInOptions {
    pub client_id: String,
    pub redirect_uri: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChatGptAuthorizationAttempt {
    pub state: String,
    pub authorize_url: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChatGptCredential {
    pub access_token: String,
    pub account_id: Option<String>,
}

pub struct ChatGptSignInClient;

#[derive(Debug, Clone, Error, PartialEq, Eq)]
#[error("{message}")]
pub struct ChatGptAuthenticationException {
    pub message: String,
}

pub struct ChatGptResponsesContract;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentModelDiscoveryRequest {
    pub provider: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentModelDiscoveryLimits {
    pub maximum_models: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentDiscoveredModel {
    pub id: String,
    pub display_name: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentModelDiscoveryResult {
    pub models: Vec<AgentDiscoveredModel>,
}

pub struct ShadowAgentProviders;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ShadowAgentBridgeRequest {
    pub model: String,
    pub prompt: String,
    pub tools: Vec<ShadowAgentBridgeTool>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ShadowAgentBridgeTool {
    pub name: String,
    pub description: String,
    pub schema: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ShadowAgentBridgeFrame {
    pub kind: String,
    pub payload: Value,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShadowAgentProviderOptions {
    pub provider: String,
    pub model: String,
    pub base_url: Option<String>,
}

#[derive(Debug, Clone, Error, PartialEq, Eq)]
#[error("{message}")]
pub struct ShadowAgentProviderException {
    pub message: String,
}

impl AgentCodeTools {
    pub fn supported_languages() -> &'static [&'static str] {
        &["rust", "python", "javascript"]
    }
}

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

impl AgentWorkspaceTools {
    pub fn search(workspace: &dyn IAgentWorkspace, needle: &str) -> Vec<AgentSearchMatch> {
        let mut hits = Vec::new();
        for info in workspace.list_files() {
            if let Some(content) = workspace.read_file(&info.path) {
                for (idx, line) in content.lines().enumerate() {
                    if line.contains(needle) {
                        hits.push(AgentSearchMatch {
                            path: info.path.clone(),
                            line: idx + 1,
                            text: line.to_string(),
                        });
                    }
                }
            }
        }
        hits
    }
}

impl ChatGptSignInClient {
    pub fn build_authorize_url(options: &ChatGptSignInOptions, state: &str) -> ChatGptAuthorizationAttempt {
        ChatGptAuthorizationAttempt {
            state: state.to_string(),
            authorize_url: format!(
                "https://auth.openai.com/oauth/authorize?client_id={}&redirect_uri={}&state={state}",
                options.client_id, options.redirect_uri
            ),
        }
    }
}

impl ChatGptResponsesContract {
    pub const ENDPOINT: &'static str = "https://api.openai.com/v1/responses";
}

impl ShadowAgentProviders {
    pub fn known_providers() -> &'static [&'static str] {
        &["openai", "anthropic", "google-genai", "codex", "acp"]
    }
}
