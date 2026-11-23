#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentHostOptions {
    pub bind_address: String,
    pub port: u16,
}

impl Default for AgentHostOptions {
    fn default() -> Self {
        Self {
            bind_address: "127.0.0.1".into(),
            port: 8787,
        }
    }
}

