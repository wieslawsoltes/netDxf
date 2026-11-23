#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use std::sync::Arc;

pub struct ShadowMcpServer {
    pub identity: ShadowMcpIdentity,
    pub registry: McpToolRegistry,
}

impl ShadowMcpServer {
    pub const INSTRUCTIONS: &'static str =
        "Use netDxf Shadow MCP tools to inspect, query, validate, and edit the CAD shadow graph deterministically.";

    pub fn new(identity: ShadowMcpIdentity) -> Self {
        Self {
            identity,
            registry: McpToolRegistry::new(),
        }
    }

    pub fn create(identity: ShadowMcpIdentity) -> Self {
        Self::new(identity)
    }

    pub fn create_options(&self) -> Value {
        serde_json::json!({
            "name": self.identity.name,
            "version": self.identity.version,
            "instructions": Self::INSTRUCTIONS,
        })
    }

    pub fn configure(&mut self, tool: Box<dyn IMcpToolExecutor>) {
        self.registry.register(tool);
    }
}

