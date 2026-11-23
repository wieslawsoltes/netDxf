#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioHttp;

impl StudioHttp {
    pub fn error(message: impl Into<String>) -> Envelope {
        Envelope {
            ok: false,
            payload: Value::Null,
            error: Some(message.into()),
        }
    }
}

