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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct McpToolDefinition {
    pub name: String,
    pub description: String,
    pub input_schema: Value,
}

impl McpToolDefinition {
    pub fn read_only(&self) -> bool {
        false
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct McpToolResult {
    pub is_error: bool,
    pub content: Value,
}

impl McpToolResult {
    pub fn text(text: impl Into<String>) -> Self {
        Self {
            is_error: false,
            content: Value::String(text.into()),
        }
    }

    pub fn from_object(content: Value) -> Self {
        Self {
            is_error: false,
            content,
        }
    }
}

pub trait IMcpToolExecutor: Send + Sync {
    fn definition(&self) -> McpToolDefinition;
    fn execute(&self, arguments: &Value) -> McpToolResult;

    fn tools(&self) -> Vec<McpToolDefinition> {
        vec![self.definition()]
    }
}

pub struct McpJson;

#[derive(Default)]
pub struct McpToolRegistry {
    tools: BTreeMap<String, Box<dyn IMcpToolExecutor>>,
}

impl McpToolRegistry {
    pub const MAXIMUM_ARGUMENT_CHARACTERS: usize = 262_144;
    pub const MAXIMUM_RESULT_CHARACTERS: usize = 524_288;

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

    pub fn tools(&self) -> Vec<McpToolDefinition> {
        self.list()
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

    pub fn execute(&self, name: &str, arguments: &Value) -> McpToolResult {
        self.call(name, arguments)
    }
}

impl McpJson {
    pub fn options() -> Value {
        serde_json::json!({ "camelCase": true, "indented": false })
    }

    pub fn empty_object() -> Value {
        serde_json::json!({})
    }

    pub fn parse(input: &str) -> Result<Value, serde_json::Error> {
        serde_json::from_str(input)
    }

    pub fn element(value: &Value) -> Value {
        value.clone()
    }

    pub fn to_string_pretty<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string_pretty(value)
    }
}

