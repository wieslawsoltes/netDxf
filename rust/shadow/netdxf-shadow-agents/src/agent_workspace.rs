#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFile {
    pub path: String,
    pub content: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFileInfo {
    pub path: String,
    pub size: usize,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFileSlice {
    pub path: String,
    pub start_line: usize,
    pub lines: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentSearchMatch {
    pub path: String,
    pub line: usize,
    pub text: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct AgentFileChange {
    pub path: String,
    pub kind: String,
}

pub trait IAgentWorkspace: Send + Sync {
    fn list_files(&self) -> Vec<AgentFileInfo>;
    fn read_file(&self, path: &str) -> Option<String>;
    fn write_file(&mut self, path: &str, content: String);
}

pub trait IAgentTransactionalWorkspace: IAgentWorkspace {
    fn changes(&self) -> Vec<AgentFileChange>;
    fn commit(&mut self);
    fn rollback(&mut self);
}

#[derive(Debug, Default)]
pub struct MemoryAgentWorkspace {
    files: BTreeMap<String, String>,
    staged_changes: Vec<AgentFileChange>,
}

impl IAgentWorkspace for MemoryAgentWorkspace {
    fn list_files(&self) -> Vec<AgentFileInfo> {
        self.files
            .iter()
            .map(|(p, c)| AgentFileInfo {
                path: p.clone(),
                size: c.len(),
            })
            .collect()
    }

    fn read_file(&self, path: &str) -> Option<String> {
        self.files.get(path).cloned()
    }

    fn write_file(&mut self, path: &str, content: String) {
        self.files.insert(path.to_string(), content);
        self.staged_changes.push(AgentFileChange {
            path: path.to_string(),
            kind: "write".into(),
        });
    }
}

impl IAgentTransactionalWorkspace for MemoryAgentWorkspace {
    fn changes(&self) -> Vec<AgentFileChange> {
        self.staged_changes.clone()
    }

    fn commit(&mut self) {
        self.staged_changes.clear();
    }

    fn rollback(&mut self) {
        self.staged_changes.clear();
    }
}

pub struct AgentWorkspaceTools;

impl AgentWorkspaceTools {
    pub fn search(workspace: &dyn IAgentWorkspace, needle: &str) -> Vec<AgentSearchMatch> {
        let mut hits = Vec::new();
        for info in workspace.list_files() {
            if let Some(content) = workspace.read_file(&info.path) {
                for (idx, line) in content.lines().enumerate() {
                    if line.contains(needle) {
                        hits.push(AgentSearchMatch {
                            path: info.path.clone(),
                            line: idx + 1,
                            text: line.to_string(),
                        });
                    }
                }
            }
        }
        hits
    }
}

