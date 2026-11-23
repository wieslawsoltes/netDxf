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

pub struct ShadowAgentProviders;

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

impl ShadowAgentProviders {
    pub fn known_providers() -> &'static [&'static str] {
        &["openai", "anthropic", "google-genai", "codex", "acp"]
    }
}

