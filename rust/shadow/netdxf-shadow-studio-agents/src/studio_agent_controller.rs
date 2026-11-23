#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct StudioAgentConfiguration {
    pub provider: String,
    pub model: String,
    pub endpoint: Option<String>,
    pub api_key: Option<String>,
    pub document_id: Option<String>,
    pub mode: String,
    pub permission_mode: String,
    pub max_turns: usize,
    pub max_no_progress_turns: usize,
    pub max_output_tokens: usize,
    pub context_window_tokens: usize,
    pub success_criteria: Option<String>,
}
