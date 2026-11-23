#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;

pub struct StudioAgentController {
    pub config: StudioAgentConfiguration,
    pub cancelled: bool,
    pub companion_connected: bool,
    pub pending_approvals: Vec<String>,
    pub workspace_files: std::collections::BTreeMap<String, String>,
}

impl StudioAgentController {
    pub fn new(config: StudioAgentConfiguration) -> Self {
        Self {
            config,
            cancelled: false,
            companion_connected: false,
            pending_approvals: Vec::new(),
            workspace_files: std::collections::BTreeMap::new(),
        }
    }

    pub fn document_id(&self) -> Option<&str> {
        self.config.document_id.as_deref()
    }

    pub fn configure(&mut self, config: StudioAgentConfiguration) {
        self.config = config;
    }

    pub fn disconnect(&mut self) {
        self.companion_connected = false;
        self.cancelled = true;
    }

    pub fn files(&self) -> Vec<String> {
        self.workspace_files.keys().cloned().collect()
    }

    pub fn apply_files(&mut self, files: std::collections::BTreeMap<String, String>) {
        for (k, v) in files {
            self.workspace_files.insert(k, v);
        }
    }

    pub fn export_files(&self) -> std::collections::BTreeMap<String, String> {
        self.workspace_files.clone()
    }

    pub fn export(&self) -> Value {
        serde_json::json!({
            "documentId": self.config.document_id,
            "provider": self.config.provider,
            "model": self.config.model,
            "fileCount": self.workspace_files.len(),
        })
    }

    pub fn call(&self, tool: &str, args: Value) -> Value {
        serde_json::json!({
            "tool": tool,
            "args": args,
            "ok": !self.cancelled,
        })
    }

    pub fn capabilities(&self) -> Vec<&'static str> {
        vec![
            "cad.read",
            "cad.edit",
            "cad.render",
            "cad.topology",
            "workspace.read",
            "workspace.write",
        ]
    }

    pub fn catalog(&self) -> Vec<String> {
        self.capabilities().into_iter().map(String::from).collect()
    }

    pub fn approve(&mut self, approval_id: &str) -> bool {
        if let Some(pos) = self.pending_approvals.iter().position(|id| id == approval_id) {
            self.pending_approvals.remove(pos);
            true
        } else {
            false
        }
    }

    pub fn cancel(&mut self) {
        self.cancelled = true;
    }

    pub fn compact(&mut self) -> bool {
        true
    }

    pub fn companion_connect(&mut self) {
        self.companion_connected = true;
    }

    pub fn companion_disconnect(&mut self) {
        self.companion_connected = false;
    }

    pub fn close_mcp_session(&mut self) {
        self.companion_connected = false;
    }
}
