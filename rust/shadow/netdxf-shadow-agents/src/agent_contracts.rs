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

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AgentOptions {
    pub permission_mode: AgentPermissionMode,
    pub run_mode: AgentRunMode,
    pub max_turns: usize,
    pub max_output_tokens: usize,
    pub max_retries: usize,
    pub max_no_progress_turns: usize,
    pub max_parallel_read_tools: usize,
    pub context_window_tokens: usize,
    pub compaction_threshold: f64,
    pub inference_timeout_secs: u64,
    pub approval_timeout_secs: u64,
    pub instructions: Option<String>,
    pub default_instructions: String,
}

impl Default for AgentOptions {
    fn default() -> Self {
        Self {
            permission_mode: AgentPermissionMode::ReadOnly,
            run_mode: AgentRunMode::Interactive,
            max_turns: 32,
            max_output_tokens: 8_192,
            max_retries: 3,
            max_no_progress_turns: 5,
            max_parallel_read_tools: 4,
            context_window_tokens: 128_000,
            compaction_threshold: 0.85,
            inference_timeout_secs: 120,
            approval_timeout_secs: 300,
            instructions: None,
            default_instructions: String::new(),
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
    pub session_id: String,
    pub status: String,
    pub running: bool,
    pub events: Vec<AgentEvent>,
    pub earliest_sequence: u64,
    pub latest_sequence: u64,
    pub next_seq: u64,
    pub context_tokens: u64,
    pub context_limit: u64,
    pub usage: AgentUsage,
    pub run: AgentRunState,
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
    pub mode: AgentRunMode,
    pub phase: String,
    pub turn: usize,
    pub continuation_count: usize,
    pub no_progress_count: usize,
    pub output_limit_count: usize,
    pub resumable: bool,
    pub stop_reason: Option<String>,
    pub detail: Option<String>,
    pub completion_summary: Option<String>,
    pub success_criteria: Option<String>,
    pub usage: AgentUsage,
    pub plan: Vec<AgentPlanStep>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentContentRecord {
    pub kind: String,
    pub text: String,
    pub name: Option<String>,
    pub call_id: Option<String>,
    pub arguments: Option<String>,
    pub is_error: bool,
    pub reasoning_provider: Option<String>,
    pub reasoning_item_id: Option<String>,
    pub protected_data: Option<String>,
    pub anthropic_redacted_thinking: Option<String>,
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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AgentCheckpoint {
    pub schema_version: u32,
    pub session_id: String,
    pub id: String,
    pub turn: usize,
    pub active_task: Option<String>,
    pub summary: Option<String>,
    pub run: AgentRunState,
    pub plan: Vec<AgentPlanStep>,
    pub pins: Vec<AgentPinnedContext>,
    pub messages: Vec<AgentMessageRecord>,
    pub receipts: Vec<AgentCallReceipt>,
    pub files: Vec<AgentFile>,
    pub archived_references: Vec<String>,
}

