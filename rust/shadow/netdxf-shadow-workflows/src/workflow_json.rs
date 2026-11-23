use crate::workflow_contracts::{WorkflowDifference, WorkflowLimits, WorkflowScenario};
use serde_json::Value;

/// Canonical JSON serialization and structural diff helpers for workflows.
pub struct WorkflowJson;

impl WorkflowJson {
    pub fn write(scenario: &WorkflowScenario) -> Result<String, serde_json::Error> {
        Self::serialize(scenario)
    }

    pub fn read(json: &str, limits: &WorkflowLimits) -> Result<WorkflowScenario, String> {
        Self::deserialize(json, limits)
    }

    pub fn serialize(scenario: &WorkflowScenario) -> Result<String, serde_json::Error> {
        serde_json::to_string_pretty(scenario)
    }

    pub fn deserialize(json: &str, limits: &WorkflowLimits) -> Result<WorkflowScenario, String> {
        if json.len() > limits.maximum_json_bytes {
            return Err("Workflow JSON exceeds configured maximum byte budget.".into());
        }
        serde_json::from_str(json).map_err(|e| e.to_string())
    }

    pub fn compare(expected: &Value, actual: &Value, max_diffs: usize) -> Vec<WorkflowDifference> {
        let mut diffs = Vec::new();
        Self::compare_rec("$", expected, actual, max_diffs, &mut diffs);
        diffs
    }

    fn compare_rec(
        path: &str,
        expected: &Value,
        actual: &Value,
        max_diffs: usize,
        out: &mut Vec<WorkflowDifference>,
    ) {
        if out.len() >= max_diffs || expected == actual {
            return;
        }
        match (expected, actual) {
            (Value::Object(eo), Value::Object(ao)) => {
                for (k, ev) in eo {
                    let next = format!("{path}.{k}");
                    match ao.get(k) {
                        Some(av) => Self::compare_rec(&next, ev, av, max_diffs, out),
                        None => out.push(WorkflowDifference {
                            path: next,
                            expected: ev.to_string(),
                            actual: "<missing>".into(),
                        }),
                    }
                    if out.len() >= max_diffs {
                        return;
                    }
                }
            }
            (Value::Array(ea), Value::Array(aa)) if ea.len() == aa.len() => {
                for (i, (ev, av)) in ea.iter().zip(aa.iter()).enumerate() {
                    Self::compare_rec(&format!("{path}[{i}]"), ev, av, max_diffs, out);
                    if out.len() >= max_diffs {
                        return;
                    }
                }
            }
            _ => out.push(WorkflowDifference {
                path: path.to_string(),
                expected: expected.to_string(),
                actual: actual.to_string(),
            }),
        }
    }
}
