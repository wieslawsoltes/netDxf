#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct EvaluationBudgetOptions {
    pub max_tokens: u64,
    pub max_cost_usd: f64,
    pub max_requests: usize,
}

impl Default for EvaluationBudgetOptions {
    fn default() -> Self {
        Self {
            max_tokens: 1_000_000,
            max_cost_usd: 10.0,
            max_requests: 100,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BudgetRequest {
    pub tokens: u64,
}

#[derive(Debug, Default)]
pub struct BudgetLedger {
    pub used_tokens: u64,
    pub requests: usize,
}

pub struct BudgetChatClient;

impl BudgetChatClient {
    pub fn charge(ledger: &mut BudgetLedger, req: &BudgetRequest, limits: &EvaluationBudgetOptions) -> Result<(), String> {
        if ledger.requests >= limits.max_requests || ledger.used_tokens + req.tokens > limits.max_tokens {
            return Err("Evaluation budget exceeded.".into());
        }
        ledger.requests += 1;
        ledger.used_tokens += req.tokens;
        Ok(())
    }
}

