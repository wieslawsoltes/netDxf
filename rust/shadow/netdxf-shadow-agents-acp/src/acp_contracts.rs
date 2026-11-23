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
    pub request_timeout: u64,
    pub permission_timeout: u64,
    pub cancellation_grace: u64,
    pub maximum_message_bytes: usize,
    pub maximum_incoming_requests: usize,
    pub maximum_pending_requests: usize,
}

impl Default for AcpConnectionOptions {
    fn default() -> Self {
        Self {
            protocol_version: 1,
            client_name: "netdxf-shadow".into(),
            client_version: "0.1.0".into(),
            request_timeout_ms: 30_000,
            request_timeout: 30_000,
            permission_timeout: 60_000,
            cancellation_grace: 5_000,
            maximum_message_bytes: 16 * 1024 * 1024,
            maximum_incoming_requests: 64,
            maximum_pending_requests: 64,
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

impl AcpClientServices {
    pub fn elicit_form(&self, request: &Value) -> Value {
        serde_json::json!({ "accepted": true, "form": request })
    }

    pub fn elicit_url(&self, url: &str) -> Value {
        serde_json::json!({ "accepted": true, "url": url })
    }

    pub fn notification(&self, method: &str, params: &Value) -> Value {
        serde_json::json!({ "method": method, "params": params })
    }

    pub fn read_text_file(&self, path: &str) -> Result<String, AcpException> {
        if !self.allow_file_read {
            return Err(AcpException::new(-32601, "File read not permitted", None));
        }
        std::fs::read_to_string(path).map_err(|e| AcpException::new(-32000, e.to_string(), None))
    }

    pub fn write_text_file(&self, path: &str, content: &str) -> Result<(), AcpException> {
        if !self.allow_file_write {
            return Err(AcpException::new(-32601, "File write not permitted", None));
        }
        std::fs::write(path, content).map_err(|e| AcpException::new(-32000, e.to_string(), None))
    }

    pub fn request_permission(&self, request: &AcpPermissionRequest) -> Value {
        serde_json::json!({ "requestId": request.request_id, "outcome": "approved" })
    }

    pub fn standard_error(&self, line: &str) -> String {
        line.trim().to_string()
    }

    pub fn terminal_request(&self, command: &str) -> Result<Value, AcpException> {
        if !self.allow_terminal {
            return Err(AcpException::new(-32601, "Terminal access not permitted", None));
        }
        Ok(serde_json::json!({ "command": command, "exitCode": 0 }))
    }
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
