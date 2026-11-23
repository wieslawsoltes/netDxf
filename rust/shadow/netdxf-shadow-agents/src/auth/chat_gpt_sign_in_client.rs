#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptSignInOptions {
    pub client_id: String,
    pub redirect_uri: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ChatGptAuthorizationAttempt {
    pub state: String,
    pub authorize_url: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptCredential {
    pub access_token: String,
    pub refresh_token: Option<String>,
    pub id_token: Option<String>,
    pub account_id: Option<String>,
    pub client_id: String,
    pub host_id: Option<String>,
    pub subject: Option<String>,
    pub email: Option<String>,
    pub scopes: Vec<String>,
    pub expires_at: Option<i64>,
    pub has_plan_usage: bool,
}

pub struct ChatGptSignInClient;

#[derive(Debug, Clone, Error, PartialEq, Eq)]
#[error("{message}")]
pub struct ChatGptAuthenticationException {
    pub message: String,
}

impl ChatGptSignInClient {
    pub fn build_authorize_url(options: &ChatGptSignInOptions, state: &str) -> ChatGptAuthorizationAttempt {
        ChatGptAuthorizationAttempt {
            state: state.to_string(),
            authorize_url: format!(
                "https://auth.openai.com/oauth/authorize?client_id={}&redirect_uri={}&state={state}",
                options.client_id, options.redirect_uri
            ),
        }
    }
}

