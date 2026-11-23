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

pub struct ShadowMcpContext {
    pub snapshot: Arc<ShadowSnapshot>,
    pub identity: ShadowMcpIdentity,
}

impl ShadowMcpContext {
    pub fn new(snapshot: Arc<ShadowSnapshot>) -> Self {
        Self {
            snapshot,
            identity: ShadowMcpIdentity::default(),
        }
    }

    pub fn document_summary(&self) -> Value {
        serde_json::json!({
            "documentId": self.snapshot.document_id,
            "revision": self.snapshot.revision,
            "objectCount": self.snapshot.objects.len(),
            "entityCount": self.snapshot.entity_ids.len(),
        })
    }

    pub fn list_entities(&self, limit: usize) -> Value {
        let items: Vec<Value> = self
            .snapshot
            .entity_ids
            .iter()
            .take(limit)
            .filter_map(|id| self.snapshot.objects.get(id))
            .map(|o| {
                let reader = self.snapshot.read(o.id.clone());
                let layer_field = reader.field("Layer");
                let layer = layer_field.text();
                serde_json::json!({
                    "id": o.id.as_str(),
                    "type": o.type_name,
                    "layer": if layer.is_empty() { "0" } else { layer },
                })
            })
            .collect();
        serde_json::json!({ "entities": items, "total": self.snapshot.entity_ids.len() })
    }

    pub fn capture_reference(&self, id: &ObjectId) -> Option<DrawingObjectReference> {
        let obj = self.snapshot.objects.get(id)?;
        let reader = self.snapshot.read(id.clone());
        Some(DrawingObjectReference {
            document_id: self.snapshot.document_id.clone(),
            revision: self.snapshot.revision,
            view_version: 0,
            layout: "Model".to_string(),
            id: id.clone(),
            object_id: id.clone(),
            definition_object_id: None,
            handle: reader.field("Handle").text().to_string(),
            type_name: obj.type_name.clone(),
            r#type: obj.short_type().to_string(),
            occurrence_key: id.as_str().to_string(),
            insert_path: Vec::new(),
        })
    }

    pub fn validate_reference(&self, reference: &DrawingObjectReference) -> bool {
        self.snapshot.document_id == reference.document_id
            && self.snapshot.objects.contains_key(&reference.object_id)
    }

    #[inline]
    pub fn source(&self) -> &Arc<ShadowSnapshot> {
        &self.snapshot
    }

    #[inline]
    pub fn editor(&self) -> &Arc<ShadowSnapshot> {
        &self.snapshot
    }

    #[inline]
    pub fn layout(&self) -> &'static str {
        "Model"
    }

    #[inline]
    pub fn changed(&self, expected_revision: i64) -> bool {
        self.snapshot.revision != expected_revision
    }

    pub fn require_workspace_identity(&self, expected_doc_id: &str) -> Result<(), String> {
        if self.snapshot.document_id == expected_doc_id {
            Ok(())
        } else {
            Err(format!(
                "Workspace identity mismatch: expected '{}', got '{}'",
                expected_doc_id, self.snapshot.document_id
            ))
        }
    }

    pub fn synchronize(&mut self, next: Arc<ShadowSnapshot>) {
        self.snapshot = next;
    }

    pub fn topology(&self) -> Value {
        serde_json::json!({
            "documentId": self.snapshot.document_id,
            "revision": self.snapshot.revision,
            "entities": self.snapshot.entity_ids.len(),
        })
    }
}

