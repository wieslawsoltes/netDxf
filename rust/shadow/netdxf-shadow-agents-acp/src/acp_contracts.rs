use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AcpProcessOptions {
    pub executable: String,
    pub working_directory: String,
    pub arguments: Vec<String>,
    pub environment: BTreeMap<String, String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AcpConnectionOptions {
    pub protocol_version: u32,
    pub client_name: String,
    pub client_version: String,
    pub request_timeout_ms: u64,
}

impl Default for AcpConnectionOptions {
    fn default() -> Self {
        Self {
            protocol_version: 1,
            client_name: "netdxf-shadow".into(),
            client_version: "0.1.0".into(),
            request_timeout_ms: 30_000,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct AcpPermissionRequest {
    pub request_id: String,
    pub session_id: String,
    pub tool_call: Value,
    pub options: Value,
    pub raw: Value,
}

#[derive(Debug, Clone, Default)]
pub struct AcpClientServices {
    pub allow_file_read: bool,
    pub allow_file_write: bool,
    pub allow_terminal: bool,
}

#[derive(Debug, Clone, Error, PartialEq)]
#[error("ACP error {code}: {message}")]
pub struct AcpException {
    pub code: i32,
    pub message: String,
    pub error_data: Option<Value>,
}

impl AcpException {
    pub fn new(code: i32, message: impl Into<String>, error_data: Option<Value>) -> Self {
        Self {
            code,
            message: message.into(),
            error_data,
        }
    }
}
