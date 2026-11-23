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
            .map(|c| Self::execute(c, target))
            .collect()
    }

    pub fn run(
        cases: &[EvaluationCase],
        target: &dyn IAgentEvaluationTarget,
    ) -> Vec<EvaluationResult> {
        Self::run_all(cases, target)
    }

    pub fn execute(
        case: &EvaluationCase,
        target: &dyn IAgentEvaluationTarget,
    ) -> EvaluationResult {
        let ctx = EvaluationFixture::context(case);
        let exec = target.evaluate(case, &ctx);
        let passed = !exec.output.is_empty();
        EvaluationResult {
            case_id: case.id.clone(),
            passed,
            checks: vec![EvaluationCheck {
                name: "non-empty-output".into(),
                passed,
                message: String::new(),
            }],
            execution: exec,
            ..Default::default()
        }
    }

    pub fn snapshot(case: &EvaluationCase) -> String {
        EvaluationFixture::freeze(case)
    }

    pub fn tools() -> Vec<&'static str> {
        vec!["cad.read", "cad.edit", "cad.render", "cad.topology"]
    }
}

