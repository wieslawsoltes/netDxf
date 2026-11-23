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

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct McpToolDefinition {
    pub name: String,
    pub description: String,
    pub input_schema: Value,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct McpToolResult {
    pub is_error: bool,
    pub content: Value,
}

pub trait IMcpToolExecutor: Send + Sync {
    fn definition(&self) -> McpToolDefinition;
    fn execute(&self, arguments: &Value) -> McpToolResult;
}

pub struct McpJson;

#[derive(Default)]
pub struct McpToolRegistry {
    tools: BTreeMap<String, Box<dyn IMcpToolExecutor>>,
}

impl McpToolRegistry {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn register(&mut self, tool: Box<dyn IMcpToolExecutor>) {
        let name = tool.definition().name;
        self.tools.insert(name, tool);
    }

    pub fn list(&self) -> Vec<McpToolDefinition> {
        self.tools.values().map(|t| t.definition()).collect()
    }

    pub fn call(&self, name: &str, arguments: &Value) -> McpToolResult {
        match self.tools.get(name) {
            Some(tool) => tool.execute(arguments),
            None => McpToolResult {
                is_error: true,
                content: serde_json::json!({ "error": format!("Unknown MCP tool '{name}'") }),
            },
        }
    }
}

impl McpJson {
    pub fn to_string_pretty<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string_pretty(value)
    }
}

