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
}

impl StudioAgentController {
    pub fn new(config: StudioAgentConfiguration) -> Self {
        Self {
            config,
            cancelled: false,
            companion_connected: false,
            pending_approvals: Vec::new(),
        }
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
