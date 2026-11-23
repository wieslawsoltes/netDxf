#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct AgentSessionEvaluationTarget;

impl IAgentEvaluationTarget for AgentSessionEvaluationTarget {
    fn evaluate(&self, case: &EvaluationCase, _ctx: &EvaluationContext) -> EvaluationExecution {
        EvaluationExecution {
            output: format!("evaluated: {}", case.id),
            tool_calls: Vec::new(),
            telemetry: EvaluationTelemetry::default(),
            ..Default::default()
        }
    }
}

