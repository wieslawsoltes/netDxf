//! Agent evaluation harness and budgets (`netDxf.Shadow.Agents.Evaluation`).

#![forbid(unsafe_code)]

use serde::{Deserialize, Serialize};
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

pub struct EvaluationCases;
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

pub struct AgentSessionEvaluationTarget;
pub struct EvaluationRunner;
