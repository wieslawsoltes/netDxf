use crate::workflow_contracts::{
    IWorkflowDriver, WorkflowLimits, WorkflowRunReport, WorkflowScenario, WorkflowStepResult,
};
use crate::workflow_json::WorkflowJson;

/// Executes a [`WorkflowScenario`] against an [`IWorkflowDriver`] and validates outputs.
pub struct WorkflowRunner;

impl WorkflowRunner {
    pub fn compare(
        expected: &serde_json::Value,
        actual: &serde_json::Value,
        max_diffs: usize,
    ) -> Vec<crate::workflow_contracts::WorkflowDifference> {
        WorkflowJson::compare(expected, actual, max_diffs)
    }

    pub fn run(
        scenario: &WorkflowScenario,
        driver: &mut dyn IWorkflowDriver,
        limits: &WorkflowLimits,
    ) -> WorkflowRunReport {
        if scenario.steps.len() > limits.maximum_steps {
            return WorkflowRunReport {
                name: scenario.name.clone(),
                passed: false,
                complete: false,
                steps: Vec::new(),
                error: Some("Scenario exceeds configured maximum step count.".into()),
            };
        }

        let mut results = Vec::with_capacity(scenario.steps.len());
        let mut all_passed = true;

        for (index, step) in scenario.steps.iter().enumerate() {
            match driver.execute(&step.operation, &step.arguments) {
                Ok(actual) => {
                    let diffs =
                        WorkflowJson::compare(&step.expected, &actual, limits.maximum_differences);
                    let passed = diffs.is_empty();
                    if !passed {
                        all_passed = false;
                    }
                    results.push(WorkflowStepResult {
                        index,
                        operation: step.operation.clone(),
                        passed,
                        differences: diffs,
                        error: None,
                    });
                }
                Err(err) => {
                    results.push(WorkflowStepResult {
                        index,
                        operation: step.operation.clone(),
                        passed: false,
                        differences: Vec::new(),
                        error: Some(err),
                    });
                    return WorkflowRunReport {
                        name: scenario.name.clone(),
                        passed: false,
                        complete: false,
                        steps: results,
                        error: Some("Stopped after step execution error.".into()),
                    };
                }
            }
        }

        WorkflowRunReport {
            name: scenario.name.clone(),
            passed: all_passed,
            complete: true,
            steps: results,
            error: None,
        }
    }
}
