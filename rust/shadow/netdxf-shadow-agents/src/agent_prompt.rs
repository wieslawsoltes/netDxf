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
pub struct AgentPrompt {
    pub text: String,
    pub pinned: Vec<AgentPinnedContext>,
}

#[derive(Debug, Default)]
pub struct AgentSession {
    pub session_id: String,
    pub options: AgentOptions,
    pub state: AgentRunState,
    pub messages: Vec<AgentMessageRecord>,
    pub pins: Vec<AgentPinnedContext>,
    pub approvals: Vec<AgentApproval>,
    pub events: Vec<AgentEvent>,
    pub workspace: MemoryAgentWorkspace,
}

impl AgentSession {
    pub fn new(options: AgentOptions) -> Self {
        Self {
            session_id: "session-1".to_string(),
            options,
            state: AgentRunState::default(),
            messages: Vec::new(),
            pins: Vec::new(),
            approvals: Vec::new(),
            events: Vec::new(),
            workspace: MemoryAgentWorkspace::default(),
        }
    }

    pub fn is_running(&self) -> bool {
        self.state.running
    }

    pub fn context_tokens(&self) -> u64 {
        let chars: usize = self
            .messages
            .iter()
            .flat_map(|m| m.content.iter())
            .map(|c| c.text.len())
            .sum();
        (chars / 4) as u64
    }

    pub fn pin(&mut self, key: impl Into<String>, content: impl Into<String>) {
        let key = key.into();
        let content = content.into();
        if let Some(existing) = self.pins.iter_mut().find(|p| p.key == key) {
            existing.content = content;
        } else {
            self.pins.push(AgentPinnedContext { key, content });
        }
    }

    pub fn unpin(&mut self, key: &str) -> bool {
        let before = self.pins.len();
        self.pins.retain(|p| p.key != key);
        self.pins.len() != before
    }

    pub fn approve(&mut self, approval_id: &str, approved: bool) -> bool {
        if let Some(item) = self.approvals.iter_mut().find(|a| a.id == approval_id) {
            item.approved = Some(approved);
            true
        } else {
            false
        }
    }

    pub fn cancel(&mut self, reason: Option<String>) {
        self.state.running = false;
        self.state.stop_reason = Some(reason.unwrap_or_else(|| "cancelled".to_string()));
    }

    pub fn compact(&mut self, summary: impl Into<String>) {
        let summary_text = summary.into();
        self.messages.clear();
        self.messages.push(AgentMessageRecord {
            role: "system".to_string(),
            content: vec![AgentContentRecord {
                kind: "summary".to_string(),
                text: summary_text,
                ..Default::default()
            }],
        });
    }

    pub fn export(&self) -> AgentCheckpoint {
        AgentCheckpoint {
            schema_version: 1,
            session_id: self.session_id.clone(),
            id: format!("{}-cp-{}", self.session_id, self.state.turn),
            turn: self.state.turn,
            active_task: None,
            summary: self.state.completion_summary.clone(),
            run: self.state.clone(),
            plan: self.state.plan.clone(),
            pins: self.pins.clone(),
            messages: self.messages.clone(),
            receipts: Vec::new(),
            files: self.workspace.export(),
            archived_references: Vec::new(),
        }
    }

    pub fn import(&mut self, checkpoint: AgentCheckpoint) {
        self.session_id = checkpoint.session_id;
        self.state = checkpoint.run;
        self.pins = checkpoint.pins;
        self.messages = checkpoint.messages;
        self.workspace.import(checkpoint.files);
    }

    pub fn poll(&self, after_seq: u64) -> AgentPollResult {
        let events: Vec<AgentEvent> = self
            .events
            .iter()
            .filter(|e| e.seq > after_seq)
            .cloned()
            .collect();
        let earliest = self.events.first().map(|e| e.seq).unwrap_or(0);
        let latest = self.events.last().map(|e| e.seq).unwrap_or(0);
        AgentPollResult {
            session_id: self.session_id.clone(),
            status: if self.state.running { "running" } else { "idle" }.to_string(),
            running: self.state.running,
            events,
            earliest_sequence: earliest,
            latest_sequence: latest,
            next_seq: latest + 1,
            context_tokens: self.context_tokens(),
            context_limit: self.options.context_window_tokens as u64,
            usage: self.state.usage,
            run: self.state.clone(),
        }
    }
}

