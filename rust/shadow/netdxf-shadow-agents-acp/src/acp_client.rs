use crate::acp_contracts::{AcpClientServices, AcpConnectionOptions, AcpException, AcpProcessOptions};
use serde_json::{json, Value};

/// Agent Client Protocol JSON-RPC client state machine.
#[derive(Debug, Clone)]
pub struct AcpClient {
    pub options: AcpConnectionOptions,
    pub services: AcpClientServices,
    pub process: Option<AcpProcessOptions>,
    next_id: u64,
}

impl AcpClient {
    pub fn new(options: AcpConnectionOptions, services: AcpClientServices) -> Self {
        Self {
            options,
            services,
            process: None,
            next_id: 1,
        }
    }

    pub fn for_process(
        process: AcpProcessOptions,
        options: AcpConnectionOptions,
        services: AcpClientServices,
    ) -> Self {
        Self {
            options,
            services,
            process: Some(process),
            next_id: 1,
        }
    }

    pub fn build_request(&mut self, method: &str, params: Value) -> Value {
        let id = self.next_id;
        self.next_id += 1;
        json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": method,
            "params": params,
        })
    }

    pub fn parse_response(response: &Value) -> Result<Value, AcpException> {
        if let Some(err) = response.get("error") {
            let code = err.get("code").and_then(Value::as_i64).unwrap_or(-32000) as i32;
            let msg = err
                .get("message")
                .and_then(Value::as_str)
                .unwrap_or("Unknown ACP error");
            return Err(AcpException::new(code, msg, err.get("data").cloned()));
        }
        Ok(response.get("result").cloned().unwrap_or(Value::Null))
    }
}
