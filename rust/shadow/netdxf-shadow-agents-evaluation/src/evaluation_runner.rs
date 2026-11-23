#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct EvaluationRunner;

impl EvaluationRunner {
    pub fn run_all(
        cases: &[EvaluationCase],
        target: &dyn IAgentEvaluationTarget,
    ) -> Vec<EvaluationResult> {
        cases
            .iter()
            .map(|c| {
                let ctx = EvaluationFixture::context(c);
                let exec = target.evaluate(c, &ctx);
                let passed = !exec.output.is_empty();
                EvaluationResult {
                    case_id: c.id.clone(),
                    passed,
                    checks: vec![EvaluationCheck {
                        name: "non-empty-output".into(),
                        passed,
                        message: String::new(),
                    }],
                    execution: exec,
                }
            })
            .collect()
    }
}

