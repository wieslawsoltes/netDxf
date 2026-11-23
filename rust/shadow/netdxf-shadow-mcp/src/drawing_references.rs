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

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingReferenceIdentity {
    pub document_id: String,
    pub revision: i64,
}

impl DrawingReferenceIdentity {
    pub fn workspace_id(&self) -> Option<&str> {
        None
    }

    pub fn view_version(&self) -> i64 {
        self.revision
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingObjectReference {
    pub document_id: String,
    pub workspace_id: Option<String>,
    pub revision: i64,
    pub view_version: i64,
    pub layout: String,
    pub id: ObjectId,
    pub object_id: ObjectId,
    pub definition_object_id: Option<ObjectId>,
    pub handle: String,
    pub type_name: String,
    pub r#type: String,
    pub occurrence_key: String,
    pub insert_path: Vec<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingReferenceCaptureRequest {
    pub document_id: String,
    pub workspace_id: Option<String>,
    pub revision: i64,
    pub view_version: i64,
    pub maximum: usize,
    pub ids: Vec<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingReferenceSet {
    pub identity: DrawingReferenceIdentity,
    pub objects: Vec<DrawingObjectReference>,
}

impl DrawingReferenceSet {
    pub fn references(&self) -> &[DrawingObjectReference] {
        &self.objects
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingReferenceValidationRequest {
    pub reference_set: DrawingReferenceSet,
}

impl DrawingReferenceValidationRequest {
    pub fn references(&self) -> &[DrawingObjectReference] {
        &self.reference_set.objects
    }

    pub fn refresh(&self) -> bool {
        false
    }
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingReferenceResolution {
    pub id: ObjectId,
    pub exists: bool,
    pub current: bool,
    pub message: Option<String>,
    pub reference: Option<DrawingObjectReference>,
    pub status: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct DrawingReferenceValidation {
    pub valid: bool,
    pub can_send: bool,
    pub identity: Option<DrawingReferenceIdentity>,
    pub items: Vec<DrawingReferenceResolution>,
    pub resolutions: Vec<DrawingReferenceResolution>,
}

pub struct DrawingReferences;

impl DrawingReferences {
    pub const MAXIMUM_COUNT: usize = 512;
    pub const VALIDATION_TOOL: &'static str = "shadow.references.validate";

    pub fn capture(snapshot: &ShadowSnapshot, request: &DrawingReferenceCaptureRequest) -> DrawingReferenceSet {
        let objects = request
            .ids
            .iter()
            .take(if request.maximum > 0 { request.maximum } else { Self::MAXIMUM_COUNT })
            .filter_map(|id| {
                snapshot.objects.get(id).map(|obj| DrawingObjectReference {
                    document_id: snapshot.document_id.clone(),
                    revision: snapshot.revision,
                    id: id.clone(),
                    object_id: id.clone(),
                    type_name: obj.type_name.clone(),
                    r#type: obj.short_type().to_string(),
                    ..Default::default()
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
            .map(|r| {
                let exists = snapshot.objects.contains_key(&r.id);
                let current = exists && r.revision == snapshot.revision;
                DrawingReferenceResolution {
                    id: r.id.clone(),
                    exists,
                    current,
                    message: if !exists {
                        Some(format!("Object {} not found", r.id))
                    } else if !current {
                        Some("Revision mismatch".to_string())
                    } else {
                        None
                    },
                    reference: Some(r.clone()),
                    status: if current {
                        "current".to_string()
                    } else if exists {
                        "stale".to_string()
                    } else {
                        "missing".to_string()
                    },
                }
            })
            .collect();
        let valid = resolutions.iter().all(|r| r.exists);
        DrawingReferenceValidation {
            valid,
            can_send: valid,
            identity: Some(request.reference_set.identity.clone()),
            items: resolutions.clone(),
            resolutions,
        }
    }

    pub fn validate_current(
        snapshot: &ShadowSnapshot,
        request: &DrawingReferenceValidationRequest,
    ) -> DrawingReferenceValidation {
        let mut validation = Self::validate(snapshot, request);
        validation.valid = validation.resolutions.iter().all(|r| r.exists && r.current);
        validation
    }

    pub fn validate_shape(reference: &DrawingObjectReference) -> Result<(), String> {
        if reference.id.0.is_empty() && reference.object_id.0.is_empty() {
            return Err("Reference must specify an object id".to_string());
        }
        Ok(())
    }
}

