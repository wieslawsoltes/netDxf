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
    pub mode: AgentRunMode,
    pub model: Option<String>,
    pub max_turns: usize,
    pub max_output_tokens: usize,
    pub max_retries: usize,
    pub max_no_progress_turns: usize,
    pub max_parallel_read_tools: usize,
    pub max_tool_calls_per_turn: usize,
    pub max_tool_result_characters: usize,
    pub context_window_tokens: usize,
    pub compaction_threshold: f64,
    pub inference_timeout_secs: u64,
    pub inference_timeout: u64,
    pub approval_timeout_secs: u64,
    pub approval_timeout: u64,
    pub tool_timeout: u64,
    pub instructions: Option<String>,
    pub default_instructions: String,
    pub success_criteria: Option<String>,
}

impl Default for AgentOptions {
    fn default() -> Self {
        Self {
            permission_mode: AgentPermissionMode::ReadOnly,
            run_mode: AgentRunMode::Interactive,
            mode: AgentRunMode::Interactive,
            model: None,
            max_turns: 32,
            max_output_tokens: 8_192,
            max_retries: 3,
            max_no_progress_turns: 5,
            max_parallel_read_tools: 4,
            max_tool_calls_per_turn: 16,
            max_tool_result_characters: 32_768,
            context_window_tokens: 128_000,
            compaction_threshold: 0.85,
            inference_timeout_secs: 120,
            inference_timeout: 120,
            approval_timeout_secs: 300,
            approval_timeout: 300,
            tool_timeout: 60,
            instructions: None,
            default_instructions: String::new(),
            success_criteria: None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AgentEvent {
    pub seq: u64,
    pub sequence: u64,
    pub kind: String,
    pub r#type: String,
    pub message: String,
    pub time: String,
    pub payload: Value,
    pub data: Value,
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
    pub cached_input_tokens: u64,
    pub cached_tokens: u64,
    pub cache_write_tokens: u64,
    pub reasoning_tokens: u64,
    pub total_tokens: u64,
    pub requests: u64,
    pub inferences: u64,
    pub inference_milliseconds: u64,
    pub first_token_milliseconds: u64,
    pub turns: u64,
    pub tool_calls: u64,
}

impl AgentUsage {
    pub fn add(&self, other: Self) -> Self {
        Self {
            input_tokens: self.input_tokens + other.input_tokens,
            output_tokens: self.output_tokens + other.output_tokens,
            cached_input_tokens: self.cached_input_tokens + other.cached_input_tokens,
            cached_tokens: self.cached_tokens + other.cached_tokens,
            cache_write_tokens: self.cache_write_tokens + other.cache_write_tokens,
            reasoning_tokens: self.reasoning_tokens + other.reasoning_tokens,
            total_tokens: self.total_tokens + other.total_tokens,
            requests: self.requests + other.requests,
            inferences: self.inferences + other.inferences,
            inference_milliseconds: self.inference_milliseconds + other.inference_milliseconds,
            first_token_milliseconds: self.first_token_milliseconds.max(other.first_token_milliseconds),
            turns: self.turns + other.turns,
            tool_calls: self.tool_calls + other.tool_calls,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentPinnedContext {
    pub key: String,
    pub content: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentApproval {
    pub id: String,
    pub request_id: String,
    pub tool: String,
    pub arguments: String,
    pub description: String,
    pub expires_at: Option<String>,
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
    pub turn_count: usize,
    pub total_turns: usize,
    pub turn_limit: usize,
    pub continuation_count: usize,
    pub no_progress_count: usize,
    pub output_limit_count: usize,
    pub resumable: bool,
    pub stop_reason: Option<String>,
    pub detail: Option<String>,
    pub completion_summary: Option<String>,
    pub success_criteria: Option<String>,
    pub verification: Option<String>,
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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentMessageRecord {
    pub message_id: String,
    pub role: String,
    pub display_text: String,
    pub content: Vec<AgentContentRecord>,
    pub contents: Vec<AgentContentRecord>,
    pub references: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AgentCallReceipt {
    pub call_id: String,
    pub tool: String,
    pub signature: String,
    pub is_error: bool,
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

