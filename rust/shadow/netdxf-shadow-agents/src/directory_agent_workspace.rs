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

use std::path::{Component, Path, PathBuf};

#[derive(Debug, Clone)]
pub struct DirectoryAgentWorkspace {
    pub root: String,
}

impl DirectoryAgentWorkspace {
    pub fn new(root: impl Into<String>) -> Self {
        Self { root: root.into() }
    }

    pub fn root(&self) -> &str {
        &self.root
    }

    fn resolve_path(&self, rel: &str) -> Result<PathBuf, String> {
        let normalized = MemoryAgentWorkspace::normalize_path(rel);
        if normalized.is_empty() || normalized.split('/').any(|s| s == "..") {
            return Err("Path escapes workspace root.".to_string());
        }
        let mut out = PathBuf::from(&self.root);
        for comp in Path::new(&normalized).components() {
            match comp {
                Component::Normal(seg) => out.push(seg),
                _ => return Err("Path escapes workspace root.".to_string()),
            }
        }
        Ok(out)
    }

    pub fn list(&self) -> Result<Vec<String>, String> {
        let root_path = Path::new(&self.root);
        if !root_path.exists() {
            return Ok(Vec::new());
        }
        let mut files = Vec::new();
        fn walk(base: &Path, dir: &Path, out: &mut Vec<String>) -> Result<(), String> {
            let entries = std::fs::read_dir(dir).map_err(|e| e.to_string())?;
            for entry in entries {
                let entry = entry.map_err(|e| e.to_string())?;
                let path = entry.path();
                if path.is_dir() {
                    walk(base, &path, out)?;
                } else if path.is_file() {
                    if let Ok(rel) = path.strip_prefix(base) {
                        out.push(rel.to_string_lossy().replace('\\', "/"));
                    }
                }
            }
            Ok(())
        }
        walk(root_path, root_path, &mut files)?;
        files.sort();
        Ok(files)
    }

    pub fn read(&self, path: &str) -> Result<String, String> {
        let full = self.resolve_path(path)?;
        std::fs::read_to_string(&full).map_err(|e| e.to_string())
    }

    pub fn write(&self, path: &str, content: &str) -> Result<(), String> {
        let full = self.resolve_path(path)?;
        if let Some(parent) = full.parent() {
            std::fs::create_dir_all(parent).map_err(|e| e.to_string())?;
        }
        std::fs::write(&full, content).map_err(|e| e.to_string())
    }

    pub fn delete(&self, path: &str) -> Result<bool, String> {
        let full = self.resolve_path(path)?;
        if full.exists() {
            std::fs::remove_file(&full).map_err(|e| e.to_string())?;
            Ok(true)
        } else {
            Ok(false)
        }
    }

    pub fn search(&self, query: &str) -> Result<Vec<(String, usize, String)>, String> {
        let needle = query.to_ascii_lowercase();
        let mut matches = Vec::new();
        for path in self.list()? {
            if let Ok(content) = self.read(&path) {
                for (idx, line) in content.lines().enumerate() {
                    if line.to_ascii_lowercase().contains(&needle) {
                        matches.push((path.clone(), idx + 1, line.to_string()));
                    }
                }
            }
        }
        Ok(matches)
    }

    pub fn export(&self) -> Result<Vec<AgentFile>, String> {
        let mut out = Vec::new();
        for path in self.list()? {
            let content = self.read(&path)?;
            out.push(AgentFile { path, content });
        }
        Ok(out)
    }

    pub fn import(&self, files: &[AgentFile]) -> Result<(), String> {
        for f in files {
            self.write(&f.path, &f.content)?;
        }
        Ok(())
    }
}

