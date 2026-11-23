#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptHostStatus {
    pub signed_in: bool,
    pub authenticated: bool,
    pub account_id: Option<String>,
    pub email: Option<String>,
    pub expires_at: Option<i64>,
    pub error: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptHostLease {
    pub lease_id: String,
    pub expires_at_unix: u64,
    pub credential: Option<String>,
    pub session_cancellation: bool,
}

pub struct ChatGptHostSession;

impl ChatGptHostSession {
    pub fn status() -> ChatGptHostStatus {
        ChatGptHostStatus::default()
    }

    pub fn begin() -> ChatGptHostStatus {
        ChatGptHostStatus::default()
    }

    pub fn complete(_code: &str) -> ChatGptHostStatus {
        ChatGptHostStatus {
            signed_in: true,
            authenticated: true,
            ..Default::default()
        }
    }

    pub fn get_lease() -> ChatGptHostLease {
        ChatGptHostLease::default()
    }

    pub fn logout() -> ChatGptHostStatus {
        ChatGptHostStatus::default()
    }
}

