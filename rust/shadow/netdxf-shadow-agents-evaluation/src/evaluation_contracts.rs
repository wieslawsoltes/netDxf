#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EvaluationCheck {
    pub name: String,
    pub passed: bool,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct EvaluationCase {
    pub id: String,
    pub description: String,
    pub prompt: String,
    pub expected: String,
    pub seed: String,
    pub grader_version: String,
}

impl EvaluationCase {
    pub fn validate(&self) -> Result<(), String> {
        if self.id.trim().is_empty() || self.prompt.trim().is_empty() {
            return Err("EvaluationCase requires non-empty id and prompt.".into());
        }
        Ok(())
    }
}

pub struct EvaluationFixture;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationTarget {
    pub id: String,
    pub name: String,
    pub provider: String,
    pub model: String,
    pub protocol: String,
    pub cache_mode: String,
    pub settings: Value,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct EvaluationContext {
    pub case_id: String,
    pub document: String,
    pub editor: String,
    pub fixture: String,
    pub workspace: String,
    pub tools: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationCost {
    pub total_usd: f64,
    pub estimated_usd: f64,
    pub reserved_usd: f64,
    pub basis: String,
    pub rate_source: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationTelemetry {
    pub duration_ms: u64,
    pub inference_milliseconds: u64,
    pub first_token_milliseconds: u64,
    pub inferences: usize,
    pub retries: usize,
    pub input_tokens: u64,
    pub output_tokens: u64,
    pub reasoning_tokens: u64,
    pub cache_read_tokens: u64,
    pub cache_write_tokens: u64,
    pub usage_complete: bool,
    pub cost: EvaluationCost,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationToolCall {
    pub tool: String,
    pub name: String,
    pub arguments: Value,
    pub is_error: bool,
    pub milliseconds: u64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationExecution {
    pub status: String,
    pub output: String,
    pub tool_calls: Vec<EvaluationToolCall>,
    pub tool_catalog_hash: String,
    pub checkpoint: Option<String>,
    pub cost: EvaluationCost,
    pub events: Vec<Value>,
    pub telemetry: EvaluationTelemetry,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationResult {
    pub schema_version: u32,
    pub grader_version: String,
    pub case_id: String,
    pub description: String,
    pub status: String,
    pub target: EvaluationTarget,
    pub repeat: usize,
    pub seed_hash: String,
    pub fingerprint: String,
    pub elapsed_milliseconds: u64,
    pub passed: bool,
    pub cost: EvaluationCost,
    pub telemetry: EvaluationTelemetry,
    pub tool_calls: usize,
    pub tool_errors: usize,
    pub tool_milliseconds: u64,
    pub tool_catalog_hash: String,
    pub tools: Vec<String>,
    pub undo_transactions: usize,
    pub checkpoint: Option<String>,
    pub events: Vec<Value>,
    pub checks: Vec<EvaluationCheck>,
    pub execution: EvaluationExecution,
}

pub trait IAgentEvaluationTarget: Send + Sync {
    fn evaluate(&self, case: &EvaluationCase, ctx: &EvaluationContext) -> EvaluationExecution;

    fn target(&self) -> EvaluationTarget {
        EvaluationTarget::default()
    }

    fn run_async(&self, case: &EvaluationCase, ctx: &EvaluationContext) -> EvaluationExecution {
        self.evaluate(case, ctx)
    }
}

impl EvaluationFixture {
    pub fn context(case: &EvaluationCase) -> EvaluationContext {
        EvaluationContext {
            case_id: case.id.clone(),
            ..Default::default()
        }
    }

    pub fn scenario(case: &EvaluationCase) -> &str {
        &case.id
    }

    pub fn seed_hash(seed: &str) -> String {
        let mut h: u64 = 0xcbf2_9ce4_8422_2325;
        for b in seed.as_bytes() {
            h ^= *b as u64;
            h = h.wrapping_mul(0x0100_0000_01b3);
        }
        format!("{h:016x}")
    }

    pub fn fingerprint(payload: &str) -> String {
        Self::seed_hash(payload)
    }

    pub fn export_seed(case: &EvaluationCase) -> String {
        format!("{}:{}", case.id, case.prompt)
    }

    pub fn freeze(case: &EvaluationCase) -> String {
        Self::fingerprint(&Self::export_seed(case))
    }

    pub fn create_drawing(case_id: &str) -> String {
        format!("drawing:{case_id}")
    }
}
