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
    pub app_name: String,
    pub client_id: String,
    pub redirect_uri: String,
    pub callback_uri: Option<String>,
    pub host_id: Option<String>,
    pub expected_subject: Option<String>,
    pub id_token_hint: Option<String>,
    pub login_hint: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ChatGptAuthorizationAttempt {
    pub state: String,
    pub authorize_url: String,
    pub authorization_uri: String,
    pub callback_uri: String,
    pub registered_client_id: String,
    pub expires_at: Option<i64>,
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
        let url = format!(
            "https://auth.openai.com/oauth/authorize?client_id={}&redirect_uri={}&state={state}",
            options.client_id, options.redirect_uri
        );
        ChatGptAuthorizationAttempt {
            state: state.to_string(),
            authorize_url: url.clone(),
            authorization_uri: url,
            callback_uri: options.redirect_uri.clone(),
            registered_client_id: options.client_id.clone(),
            expires_at: None,
        }
    }

    pub fn begin(options: &ChatGptSignInOptions, state: &str) -> ChatGptAuthorizationAttempt {
        Self::build_authorize_url(options, state)
    }

    pub fn complete(options: &ChatGptSignInOptions, code: &str) -> ChatGptCredential {
        ChatGptCredential {
            access_token: format!("tok_{code}"),
            client_id: options.client_id.clone(),
            ..Default::default()
        }
    }

    pub fn refresh(credential: &ChatGptCredential) -> ChatGptCredential {
        credential.clone()
    }

    pub fn revoke(_credential: &ChatGptCredential) -> bool {
        true
    }
}

