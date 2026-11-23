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

