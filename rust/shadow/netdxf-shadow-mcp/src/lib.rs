//! Model Context Protocol (MCP) server and CAD tool registry (`netDxf.Shadow.Mcp`).

#![forbid(unsafe_code)]

use netdxf_shadow::{ObjectId, ShadowSnapshot};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
use std::sync::Arc;

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingReferenceIdentity {
    pub document_id: String,
    pub revision: i64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingObjectReference {
    pub id: ObjectId,
    pub type_name: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingReferenceCaptureRequest {
    pub ids: Vec<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingReferenceSet {
    pub identity: DrawingReferenceIdentity,
    pub objects: Vec<DrawingObjectReference>,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingReferenceValidationRequest {
    pub reference_set: DrawingReferenceSet,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingReferenceResolution {
    pub id: ObjectId,
    pub exists: bool,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DrawingReferenceValidation {
    pub valid: bool,
    pub resolutions: Vec<DrawingReferenceResolution>,
}

pub struct DrawingReferences;

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
pub struct ShadowJson;

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Location {
    pub x: f64,
    pub y: f64,
    pub z: f64,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ShadowMcpIdentity {
    pub name: String,
    pub version: String,
}

impl Default for ShadowMcpIdentity {
    fn default() -> Self {
        Self {
            name: "netdxf-shadow-mcp".into(),
            version: "0.1.0".into(),
        }
    }
}

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

pub struct McpJsonRpcSession {
    pub registry: McpToolRegistry,
}

impl McpJsonRpcSession {
    pub fn new(registry: McpToolRegistry) -> Self {
        Self { registry }
    }

    pub fn handle_request(&self, request: &Value) -> Value {
        let id = request.get("id").cloned().unwrap_or(Value::Null);
        let method = request
            .get("method")
            .and_then(Value::as_str)
            .unwrap_or("");
        match method {
            "initialize" => serde_json::json!({
                "jsonrpc": "2.0",
                "id": id,
                "result": {
                    "protocolVersion": "2024-11-05",
                    "serverInfo": ShadowMcpIdentity::default(),
                    "capabilities": { "tools": {} }
                }
            }),
            "tools/list" => serde_json::json!({
                "jsonrpc": "2.0",
                "id": id,
                "result": { "tools": self.registry.list() }
            }),
            "tools/call" => {
                let params = request.get("params").cloned().unwrap_or(Value::Null);
                let name = params.get("name").and_then(Value::as_str).unwrap_or("");
                let args = params.get("arguments").cloned().unwrap_or(Value::Null);
                let res = self.registry.call(name, &args);
                serde_json::json!({
                    "jsonrpc": "2.0",
                    "id": id,
                    "result": res
                })
            }
            _ => serde_json::json!({
                "jsonrpc": "2.0",
                "id": id,
                "error": { "code": -32601, "message": format!("Method not found: {method}") }
            }),
        }
    }
}

pub struct ShadowMcpContext {
    pub snapshot: Arc<ShadowSnapshot>,
}

pub struct ShadowMcpServer {
    pub identity: ShadowMcpIdentity,
    pub registry: McpToolRegistry,
}

impl ShadowMcpServer {
    pub fn new(identity: ShadowMcpIdentity) -> Self {
        Self {
            identity,
            registry: McpToolRegistry::new(),
        }
    }
}

pub struct ShadowMcpTools;

impl DrawingReferences {
    pub fn capture(snapshot: &ShadowSnapshot, request: &DrawingReferenceCaptureRequest) -> DrawingReferenceSet {
        let objects = request
            .ids
            .iter()
            .filter_map(|id| {
                snapshot.objects.get(id).map(|obj| DrawingObjectReference {
                    id: id.clone(),
                    type_name: obj.type_name.clone(),
                })
            })
            .collect();
        DrawingReferenceSet {
            identity: DrawingReferenceIdentity {
                document_id: snapshot.document_id.clone(),
                revision: snapshot.revision,
            },
            objects,
        }
    }

    pub fn validate(
        snapshot: &ShadowSnapshot,
        request: &DrawingReferenceValidationRequest,
    ) -> DrawingReferenceValidation {
        let resolutions: Vec<DrawingReferenceResolution> = request
            .reference_set
            .objects
            .iter()
            .map(|r| DrawingReferenceResolution {
                id: r.id.clone(),
                exists: snapshot.objects.contains_key(&r.id),
            })
            .collect();
        let valid = resolutions.iter().all(|r| r.exists);
        DrawingReferenceValidation { valid, resolutions }
    }
}

impl McpJson {
    pub fn to_string_pretty<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string_pretty(value)
    }
}

impl ShadowJson {
    pub fn snapshot_summary(snapshot: &ShadowSnapshot) -> Value {
        serde_json::json!({
            "documentId": snapshot.document_id,
            "revision": snapshot.revision,
            "objectCount": snapshot.objects.len(),
            "entityCount": snapshot.entity_ids.len(),
        })
    }
}

impl ShadowMcpTools {
    pub fn standard_tool_names() -> &'static [&'static str] {
        &[
            "shadow.document.summary",
            "shadow.entities.list",
            "shadow.references.capture",
            "shadow.references.validate",
        ]
    }
}
