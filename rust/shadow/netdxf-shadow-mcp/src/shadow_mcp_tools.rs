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

pub struct ShadowMcpTools;

impl ShadowMcpTools {
    pub fn standard_tool_names() -> &'static [&'static str] {
        &[
            "shadow.document.summary",
            "shadow.entities.list",
            "shadow.references.capture",
            "shadow.references.validate",
        ]
    }

    pub fn invoke(ctx: &ShadowMcpContext, tool: &str, args: &Value) -> Result<Value, String> {
        match tool {
            "shadow.document.summary" => Ok(ctx.document_summary()),
            "shadow.entities.list" => {
                let limit = args
                    .get("limit")
                    .and_then(Value::as_u64)
                    .map(|v| v as usize)
                    .unwrap_or(200);
                Ok(ctx.list_entities(limit))
            }
            "shadow.references.capture" => {
                let id_str = args
                    .get("objectId")
                    .and_then(Value::as_str)
                    .ok_or_else(|| "Missing objectId".to_string())?;
                let id = ObjectId::new(id_str);
                let reference = ctx
                    .capture_reference(&id)
                    .ok_or_else(|| format!("Object '{id_str}' not found"))?;
                serde_json::to_value(reference).map_err(|e| e.to_string())
            }
            "shadow.references.validate" => {
                let reference: DrawingObjectReference =
                    serde_json::from_value(args.clone()).map_err(|e| e.to_string())?;
                Ok(serde_json::json!({ "valid": ctx.validate_reference(&reference) }))
            }
            _ => Err(format!("Unknown MCP tool '{tool}'")),
        }
    }
}

