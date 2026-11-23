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

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EvaluationCase {
    pub id: String,
    pub prompt: String,
}

pub struct EvaluationFixture;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EvaluationTarget {
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct EvaluationContext {
    pub case_id: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationCost {
    pub total_usd: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct EvaluationTelemetry {
    pub duration_ms: u64,
    pub cost: EvaluationCost,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EvaluationToolCall {
    pub tool: String,
    pub arguments: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EvaluationExecution {
    pub output: String,
    pub tool_calls: Vec<EvaluationToolCall>,
    pub telemetry: EvaluationTelemetry,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct EvaluationResult {
    pub case_id: String,
    pub passed: bool,
    pub checks: Vec<EvaluationCheck>,
    pub execution: EvaluationExecution,
}

pub trait IAgentEvaluationTarget: Send + Sync {
    fn evaluate(&self, case: &EvaluationCase, ctx: &EvaluationContext) -> EvaluationExecution;
}

impl EvaluationFixture {
    pub fn context(case: &EvaluationCase) -> EvaluationContext {
        EvaluationContext {
            case_id: case.id.clone(),
        }
    }
}

