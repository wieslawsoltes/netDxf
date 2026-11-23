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
pub struct AgentModelDiscoveryRequest {
    pub provider: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentModelDiscoveryLimits {
    pub maximum_models: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentDiscoveredModel {
    pub id: String,
    pub display_name: String,
    pub context_window_tokens: Option<usize>,
    pub input_token_limit: Option<usize>,
    pub output_token_limit: Option<usize>,
    pub metadata_source: Option<String>,
    pub metadata_url: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct AgentModelDiscoveryResult {
    pub provider: String,
    pub status: String,
    pub notice: Option<String>,
    pub truncated: bool,
    pub models: Vec<AgentDiscoveredModel>,
}

