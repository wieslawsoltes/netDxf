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

