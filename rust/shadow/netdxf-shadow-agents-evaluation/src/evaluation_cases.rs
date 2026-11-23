#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct EvaluationCases;

impl EvaluationCases {
    pub fn default_suite() -> Vec<EvaluationCase> {
        vec![EvaluationCase {
            id: "smoke-document-summary".into(),
            prompt: "Summarize the active CAD document.".into(),
        }]
    }
}

