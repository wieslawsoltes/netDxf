#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptHostStatus {
    pub signed_in: bool,
    pub account_id: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChatGptHostLease {
    pub lease_id: String,
    pub expires_at_unix: u64,
}

pub struct ChatGptHostSession;

impl ChatGptHostSession {
    pub fn status() -> ChatGptHostStatus {
        ChatGptHostStatus::default()
    }
}

