#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Envelope {
    pub ok: bool,
    pub payload: Value,
    pub error: Option<String>,
}

pub struct StudioAgentHttp;

impl StudioAgentHttp {
    pub fn ok(payload: Value) -> Envelope {
        Envelope {
            ok: true,
            payload,
            error: None,
        }
    }
}

