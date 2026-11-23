use crate::codex_contracts::{CodexProcessOptions, CodexRpcException};
use serde_json::{json, Value};

/// Codex app-server JSON-RPC client.
#[derive(Debug, Clone)]
pub struct CodexAppServerClient {
    pub options: CodexProcessOptions,
    next_id: u64,
}

impl CodexAppServerClient {
    pub fn new(options: CodexProcessOptions) -> Self {
        Self {
            options,
            next_id: 1,
        }
    }

    pub fn build_rpc_request(&mut self, method: &str, params: Value) -> Value {
        let id = self.next_id;
        self.next_id += 1;
        json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": method,
            "params": params,
        })
    }

    pub fn parse_rpc_response(response: &Value) -> Result<Value, CodexRpcException> {
        if let Some(err) = response.get("error") {
            let code = err.get("code").and_then(Value::as_i64).unwrap_or(-32000) as i32;
            let msg = err
                .get("message")
                .and_then(Value::as_str)
                .unwrap_or("Unknown Codex RPC error");
            return Err(CodexRpcException::new(code, msg, err.get("data").cloned()));
        }
        Ok(response.get("result").cloned().unwrap_or(Value::Null))
    }
}
