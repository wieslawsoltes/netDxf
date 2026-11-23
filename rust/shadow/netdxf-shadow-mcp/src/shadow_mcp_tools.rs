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
            "shadow.object.properties",
            "shadow.object.incoming",
            "shadow.object.inline",
            "shadow.objects.ordered",
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
            "shadow.object.properties" => {
                let id_str = args
                    .get("objectId")
                    .and_then(Value::as_str)
                    .ok_or_else(|| "Missing objectId".to_string())?;
                let props = Self::properties(ctx, &ObjectId::new(id_str));
                serde_json::to_value(props).map_err(|e| e.to_string())
            }
            "shadow.object.incoming" => {
                let id_str = args
                    .get("objectId")
                    .and_then(Value::as_str)
                    .ok_or_else(|| "Missing objectId".to_string())?;
                let incoming: Vec<String> = Self::incoming(ctx, &ObjectId::new(id_str))
                    .into_iter()
                    .map(|o| o.0)
                    .collect();
                Ok(serde_json::json!({ "incoming": incoming }))
            }
            "shadow.object.inline" => {
                let id_str = args
                    .get("objectId")
                    .and_then(Value::as_str)
                    .ok_or_else(|| "Missing objectId".to_string())?;
                Self::inline(ctx, &ObjectId::new(id_str))
                    .ok_or_else(|| format!("Object '{id_str}' not found"))
            }
            "shadow.objects.ordered" => {
                let ids: Vec<String> = Self::ordered(ctx).into_iter().map(|o| o.0).collect();
                Ok(serde_json::json!({ "objects": ids }))
            }
            _ => Err(format!("Unknown MCP tool '{tool}'")),
        }
    }

    pub fn register() -> Vec<&'static str> {
        Self::standard_tool_names().to_vec()
    }

    pub fn count(ctx: &ShadowMcpContext) -> usize {
        ctx.snapshot.entity_ids.len()
    }

    pub fn count_object(ctx: &ShadowMcpContext) -> usize {
        ctx.snapshot.objects.len()
    }

    pub fn create(ctx: &ShadowMcpContext, id: &ObjectId) -> Option<DrawingObjectReference> {
        ctx.capture_reference(id)
    }

    pub fn incoming(ctx: &ShadowMcpContext, id: &ObjectId) -> Vec<ObjectId> {
        let mut out = Vec::new();
        for (oid, obj) in &ctx.snapshot.objects {
            for val in obj.properties.values() {
                if let netdxf_shadow::ShadowValue::Reference(target) = val {
                    if &target.id == id {
                        out.push(oid.clone());
                    }
                }
            }
        }
        out
    }

    pub fn inline(ctx: &ShadowMcpContext, id: &ObjectId) -> Option<Value> {
        let obj = ctx.snapshot.objects.get(id)?;
        Some(serde_json::json!({
            "id": obj.id.as_str(),
            "type": obj.type_name,
            "propertyCount": obj.properties.len(),
        }))
    }

    pub fn ordered(ctx: &ShadowMcpContext) -> Vec<ObjectId> {
        let mut ids: Vec<ObjectId> = ctx.snapshot.objects.keys().cloned().collect();
        ids.sort();
        ids
    }

    pub fn properties(ctx: &ShadowMcpContext, id: &ObjectId) -> BTreeMap<String, String> {
        let mut map = BTreeMap::new();
        if let Some(obj) = ctx.snapshot.objects.get(id) {
            let reader = ctx.snapshot.read(obj.id.clone());
            for k in obj.properties.keys() {
                let f = reader.field(k);
                map.insert(k.clone(), f.text().to_string());
            }
        }
        map
    }
}

