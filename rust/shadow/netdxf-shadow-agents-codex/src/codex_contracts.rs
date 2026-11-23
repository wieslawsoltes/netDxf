use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct CodexProcessOptions {
    pub executable: String,
    pub working_directory: String,
    pub codex_home: Option<String>,
    pub maximum_message_bytes: usize,
    pub request_timeout: u64,
    pub arguments: Vec<String>,
    pub environment: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CodexNotification {
    pub method: String,
    pub parameters: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CodexServerRequest {
    pub id: Value,
    pub method: String,
    pub parameters: Value,
}

#[derive(Debug, Clone, Error, PartialEq)]
#[error("Codex RPC error {code}: {message}")]
pub struct CodexRpcException {
    pub code: i32,
    pub message: String,
    pub data: Option<Value>,
}

impl CodexRpcException {
    pub fn new(code: i32, message: impl Into<String>, data: Option<Value>) -> Self {
        Self {
            code,
            message: message.into(),
            data,
        }
    }

    pub fn error_data(&self) -> Option<&Value> {
        self.data.as_ref()
    }
}
