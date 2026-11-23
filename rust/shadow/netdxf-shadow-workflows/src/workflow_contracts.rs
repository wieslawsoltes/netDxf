use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct WorkflowBaseline {
    pub kind: String,
    pub data: String,
    pub sha256: String,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WorkflowStep {
    pub operation: String,
    pub arguments: Value,
    pub expected: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WorkflowScenario {
    pub version: i32,
    pub name: String,
    pub baseline: WorkflowBaseline,
    pub steps: Vec<WorkflowStep>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct WorkflowLimits {
    pub maximum_steps: usize,
    pub maximum_json_bytes: usize,
    pub maximum_differences: usize,
    pub numeric_tolerance: u64,
}

impl Default for WorkflowLimits {
    fn default() -> Self {
        Self {
            maximum_steps: 1_000,
            maximum_json_bytes: 32 * 1024 * 1024,
            maximum_differences: 100,
            numeric_tolerance: 0,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct WorkflowDifference {
    pub path: String,
    pub expected: String,
    pub actual: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct WorkflowStepResult {
    pub index: usize,
    pub operation: String,
    pub passed: bool,
    pub differences: Vec<WorkflowDifference>,
    pub error: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct WorkflowRunReport {
    pub name: String,
    pub passed: bool,
    pub complete: bool,
    pub steps: Vec<WorkflowStepResult>,
    pub error: Option<String>,
}

/// Execution backend for replaying workflow steps.
pub trait IWorkflowDriver {
    fn execute(&mut self, operation: &str, arguments: &Value) -> Result<Value, String>;
}

/// Records executed operations into a [`WorkflowScenario`].
#[derive(Debug, Clone)]
pub struct WorkflowRecorder {
    pub name: String,
    pub baseline: WorkflowBaseline,
    pub steps: Vec<WorkflowStep>,
}

impl WorkflowRecorder {
    pub fn new(name: impl Into<String>, baseline: WorkflowBaseline) -> Self {
        Self {
            name: name.into(),
            baseline,
            steps: Vec::new(),
        }
    }

    pub fn record(&mut self, operation: impl Into<String>, arguments: Value, expected: Value) {
        self.steps.push(WorkflowStep {
            operation: operation.into(),
            arguments,
            expected,
        });
    }

    pub fn build(&self) -> WorkflowScenario {
        WorkflowScenario {
            version: 1,
            name: self.name.clone(),
            baseline: self.baseline.clone(),
            steps: self.steps.clone(),
        }
    }
}
