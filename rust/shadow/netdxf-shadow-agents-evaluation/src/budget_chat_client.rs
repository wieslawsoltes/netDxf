#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EvaluationBudgetOptions {
    pub provider: String,
    pub model: String,
    pub rate_source: String,
    pub input_rate: f64,
    pub output_rate: f64,
    pub context_tokens: u64,
    pub max_output_tokens: u64,
    pub cache_tokens_additional_to_input: bool,
    pub max_tokens: u64,
    pub max_cost_usd: f64,
    pub max_requests: usize,
}

impl Default for EvaluationBudgetOptions {
    fn default() -> Self {
        Self {
            provider: String::new(),
            model: String::new(),
            rate_source: "default".into(),
            input_rate: 0.0,
            output_rate: 0.0,
            context_tokens: 128_000,
            max_output_tokens: 8_192,
            cache_tokens_additional_to_input: false,
            max_tokens: 1_000_000,
            max_cost_usd: 10.0,
            max_requests: 100,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BudgetRequest {
    pub provider: String,
    pub model: String,
    pub tokens: u64,
    pub input_tokens: u64,
    pub output_tokens: u64,
    pub input_token_upper_bound: u64,
    pub output_token_upper_bound: u64,
    pub input_rate: f64,
    pub output_rate: f64,
    pub reserved_usd: f64,
    pub charged_usd: f64,
}

#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct BudgetLedger {
    pub schema_version: u32,
    pub used_tokens: u64,
    pub used_usd: f64,
    pub charged_usd: f64,
    pub maximum_usd: f64,
    pub requests: usize,
}

#[derive(Debug, Clone, Default)]
pub struct BudgetChatClient {
    pub observed_cost: f64,
}

impl BudgetChatClient {
    pub fn charge(
        ledger: &mut BudgetLedger,
        req: &BudgetRequest,
        limits: &EvaluationBudgetOptions,
    ) -> Result<(), String> {
        let cost = req.charged_usd.max(req.reserved_usd);
        if ledger.requests >= limits.max_requests
            || ledger.used_tokens + req.tokens > limits.max_tokens
            || ledger.used_usd + cost > limits.max_cost_usd
        {
            return Err("Evaluation budget exceeded.".into());
        }
        ledger.requests += 1;
        ledger.used_tokens += req.tokens;
        ledger.used_usd += cost;
        ledger.charged_usd += cost;
        Ok(())
    }

    pub fn get_response(&mut self, prompt: &str) -> Value {
        serde_json::json!({ "prompt": prompt, "cost": self.observed_cost })
    }

    pub fn get_streaming_response(&mut self, prompt: &str) -> Vec<Value> {
        vec![self.get_response(prompt)]
    }
}
