//! Deterministic workflow recording and replay (`netDxf.Shadow.Workflows`).

#![forbid(unsafe_code)]

pub mod workflow_contracts;
pub mod workflow_json;
pub mod workflow_runner;

pub use workflow_contracts::{
    IWorkflowDriver, WorkflowBaseline, WorkflowDifference, WorkflowLimits, WorkflowRecorder,
    WorkflowRunReport, WorkflowScenario, WorkflowStep, WorkflowStepResult,
};
pub use workflow_json::WorkflowJson;
pub use workflow_runner::WorkflowRunner;
