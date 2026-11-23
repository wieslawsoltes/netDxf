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

    fn list(&self) -> Vec<AgentFileInfo> {
        self.list_files()
    }

    fn read(&self, path: &str) -> Option<String> {
        self.read_file(path)
    }

    fn write(&mut self, path: &str, content: String) {
        self.write_file(path, content);
    }

    fn delete(&mut self, _path: &str) -> bool {
        false
    }

    fn search(&self, needle: &str) -> Vec<AgentSearchMatch>
    where
        Self: Sized,
    {
        let mut hits = Vec::new();
        for info in self.list_files() {
            if let Some(content) = self.read_file(&info.path) {
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

    fn export(&self) -> Vec<AgentFile> {
        self.list_files()
            .into_iter()
            .filter_map(|info| {
                self.read_file(&info.path).map(|content| AgentFile {
                    path: info.path,
                    content,
                })
            })
            .collect()
    }

    fn import(&mut self, files: Vec<AgentFile>) {
        for f in files {
            self.write_file(&f.path, f.content);
        }
    }
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

impl MemoryAgentWorkspace {
    pub fn normalize_path(path: &str) -> String {
        path.replace('\\', "/").trim_start_matches('/').to_string()
    }

    pub fn list(&self) -> Vec<AgentFileInfo> {
        self.list_files()
    }

    pub fn read(&self, path: &str) -> Option<String> {
        let norm = Self::normalize_path(path);
        self.files.get(&norm).or_else(|| self.files.get(path)).cloned()
    }

    pub fn write(&mut self, path: &str, content: impl Into<String>) {
        let norm = Self::normalize_path(path);
        self.write_file(&norm, content.into());
    }

    pub fn delete(&mut self, path: &str) -> bool {
        let norm = Self::normalize_path(path);
        let removed = self.files.remove(&norm).is_some() || self.files.remove(path).is_some();
        if removed {
            self.staged_changes.push(AgentFileChange {
                path: norm,
                kind: "delete".into(),
            });
        }
        removed
    }

    pub fn search(&self, needle: &str) -> Vec<AgentSearchMatch> {
        AgentWorkspaceTools::search(self, needle)
    }

    pub fn apply(&mut self, files: &[AgentFile]) {
        for f in files {
            self.write(&f.path, f.content.clone());
        }
    }

    pub fn export(&self) -> Vec<AgentFile> {
        self.files
            .iter()
            .map(|(path, content)| AgentFile {
                path: path.clone(),
                content: content.clone(),
            })
            .collect()
    }

    pub fn import(&mut self, files: Vec<AgentFile>) {
        self.files.clear();
        for f in files {
            self.files.insert(Self::normalize_path(&f.path), f.content);
        }
    }
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

