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

pub struct ShadowAgentProviders;

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ShadowAgentProviderOptions {
    pub provider: String,
    pub model: String,
    pub base_url: Option<String>,
}

#[derive(Debug, Clone, Error, PartialEq, Eq)]
#[error("{message}")]
pub struct ShadowAgentProviderException {
    pub message: String,
}

impl ShadowAgentProviders {
    pub fn known_providers() -> &'static [&'static str] {
        &["openai", "anthropic", "google-genai", "codex", "acp"]
    }

    pub fn create(options: ShadowAgentProviderOptions) -> Result<ShadowAgentProviderOptions, ShadowAgentProviderException> {
        if options.provider.trim().is_empty() {
            return Err(ShadowAgentProviderException {
                message: "Provider name is required.".into(),
            });
        }
        Ok(options)
    }

    pub fn create_browser(model: impl Into<String>) -> ShadowAgentProviderOptions {
        ShadowAgentProviderOptions {
            provider: "browser".into(),
            model: model.into(),
            base_url: None,
        }
    }

    pub fn create_browser_bridge(model: impl Into<String>, base_url: impl Into<String>) -> ShadowAgentProviderOptions {
        ShadowAgentProviderOptions {
            provider: "browser-bridge".into(),
            model: model.into(),
            base_url: Some(base_url.into()),
        }
    }

    pub fn discover_models(provider: &str) -> Vec<String> {
        match provider {
            "openai" => vec!["gpt-5".into(), "gpt-4.1".into()],
            "anthropic" => vec!["claude-sonnet-4".into()],
            "google-genai" => vec!["gemini-2.5-pro".into(), "gemini-2.5-flash".into()],
            _ => vec!["default".into()],
        }
    }

    pub fn discover_browser_models() -> Vec<String> {
        Self::discover_models("browser")
    }

    pub fn get_response(options: &ShadowAgentProviderOptions, prompt: &str) -> Value {
        serde_json::json!({
            "provider": options.provider,
            "model": options.model,
            "prompt": prompt,
        })
    }

    pub fn get_streaming_response(options: &ShadowAgentProviderOptions, prompt: &str) -> Vec<Value> {
        vec![Self::get_response(options, prompt)]
    }

    pub fn get_service<'a>(options: &'a ShadowAgentProviderOptions, name: &str) -> Option<&'a str> {
        match name {
            "provider" => Some(&options.provider),
            "model" => Some(&options.model),
            "baseUrl" => options.base_url.as_deref(),
            _ => None,
        }
    }

    pub fn try_get_transient_failure(status_code: u16) -> Option<&'static str> {
        match status_code {
            429 => Some("rate_limited"),
            500 | 502 | 503 | 504 => Some("upstream_unavailable"),
            _ => None,
        }
    }
}

