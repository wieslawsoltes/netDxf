use crate::codex_contracts::{CodexProcessOptions, CodexRpcException};
use serde_json::{json, Value};

/// Codex app-server JSON-RPC client.
#[derive(Debug, Clone)]
pub struct CodexAppServerClient {
    pub options: CodexProcessOptions,
    pub initialized: bool,
    pub connection_error: Option<String>,
    next_id: u64,
}

impl CodexAppServerClient {
    pub fn new(options: CodexProcessOptions) -> Self {
        Self {
            options,
            initialized: false,
            connection_error: None,
            next_id: 1,
        }
    }

    #[inline]
    pub fn is_initialized(&self) -> bool {
        self.initialized
    }

    pub fn build_initialize_request(&mut self) -> Value {
        self.initialized = true;
        self.build_rpc_request("initialize", json!({ "clientInfo": { "name": "netdxf-shadow", "version": "0.1.0" } }))
    }

    pub fn build_list_models_request(&mut self) -> Value {
        self.build_rpc_request("model/list", json!({}))
    }

    pub fn build_begin_chat_gpt_login_request(&mut self) -> Value {
        self.build_rpc_request("account/login/start", json!({ "type": "chatgpt" }))
    }

    pub fn build_cancel_login_request(&mut self, login_id: &str) -> Value {
        self.build_rpc_request("account/login/cancel", json!({ "loginId": login_id }))
    }

    pub fn build_compact_thread_request(&mut self, thread_id: &str) -> Value {
        self.build_rpc_request("thread/compact", json!({ "threadId": thread_id }))
    }

    pub fn build_interrupt_turn_request(&mut self, thread_id: &str, turn_id: &str) -> Value {
        self.build_rpc_request(
            "turn/interrupt",
            json!({ "threadId": thread_id, "turnId": turn_id }),
        )
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

    pub fn start(&mut self) -> Result<(), CodexRpcException> {
        self.connection_error = None;
        Ok(())
    }

    pub fn initialize(&mut self) -> Result<Value, CodexRpcException> {
        Ok(self.build_initialize_request())
    }

    pub fn call(&mut self, method: &str, params: Value) -> Result<Value, CodexRpcException> {
        Ok(self.build_rpc_request(method, params))
    }

    pub fn list_models(&mut self) -> Result<Value, CodexRpcException> {
        Ok(self.build_list_models_request())
    }

    pub fn begin_chat_gpt_login(&mut self) -> Result<Value, CodexRpcException> {
        Ok(self.build_begin_chat_gpt_login_request())
    }

    pub fn cancel_login(&mut self, login_id: &str) -> Result<Value, CodexRpcException> {
        Ok(self.build_cancel_login_request(login_id))
    }

    pub fn logout(&mut self) -> Result<Value, CodexRpcException> {
        Ok(self.build_rpc_request("account/logout", json!({})))
    }

    pub fn start_thread(&mut self, params: Value) -> Result<Value, CodexRpcException> {
        Ok(self.build_rpc_request("thread/start", params))
    }

    pub fn start_turn(&mut self, thread_id: &str, params: Value) -> Result<Value, CodexRpcException> {
        Ok(self.build_rpc_request("turn/start", json!({ "threadId": thread_id, "input": params })))
    }

    pub fn compact_thread(&mut self, thread_id: &str) -> Result<Value, CodexRpcException> {
        Ok(self.build_compact_thread_request(thread_id))
    }

    pub fn interrupt_turn(&mut self, thread_id: &str, turn_id: &str) -> Result<Value, CodexRpcException> {
        Ok(self.build_interrupt_turn_request(thread_id, turn_id))
    }

    pub fn respond(&self, id: u64, result: Value) -> Value {
        json!({ "jsonrpc": "2.0", "id": id, "result": result })
    }

    pub fn respond_error(&self, id: u64, code: i32, message: &str) -> Value {
        json!({ "jsonrpc": "2.0", "id": id, "error": { "code": code, "message": message } })
    }

    pub fn notification(&self, method: &str, params: Value) -> Value {
        json!({ "jsonrpc": "2.0", "method": method, "params": params })
    }

    #[inline]
    pub fn completion(&self) -> bool {
        self.connection_error.is_none()
    }
}
