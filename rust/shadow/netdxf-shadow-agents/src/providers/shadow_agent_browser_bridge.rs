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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ShadowAgentBridgeRequest {
    pub name: String,
    pub description: String,
    pub provider: String,
    pub endpoint: Option<String>,
    pub api_key: Option<String>,
    pub model: String,
    pub prompt: String,
    pub prompt_cache_key: Option<String>,
    pub optimize_prompt_caching: bool,
    pub options: Value,
    pub messages: Vec<Value>,
    pub tools: Vec<ShadowAgentBridgeTool>,
    pub json_schema: Option<Value>,
    pub json_options: Option<Value>,
    pub additional_properties: BTreeMap<String, Value>,
}

impl ShadowAgentBridgeRequest {
    pub fn create(model: impl Into<String>, prompt: impl Into<String>) -> Self {
        Self {
            model: model.into(),
            prompt: prompt.into(),
            ..Default::default()
        }
    }

    pub fn create_options(&self) -> Value {
        serde_json::json!({
            "model": self.model,
            "endpoint": self.endpoint,
            "toolCount": self.tools.len(),
        })
    }

    pub fn validate(&self) -> Result<(), String> {
        if self.model.trim().is_empty() {
            Err("Model is required.".into())
        } else {
            Ok(())
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ShadowAgentBridgeTool {
    pub name: String,
    pub description: String,
    pub schema: Value,
}

impl ShadowAgentBridgeTool {
    pub fn input_schema(&self) -> &Value {
        &self.schema
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ShadowAgentBridgeFrame {
    pub r#type: String,
    pub kind: String,
    pub code: Option<String>,
    pub message: Option<String>,
    pub is_transient: bool,
    pub retry_after_seconds: Option<u64>,
    pub update: Option<Value>,
    pub payload: Value,
}

