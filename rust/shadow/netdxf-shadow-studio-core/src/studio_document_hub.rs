#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{ObjectId, ShadowSnapshot, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadScene, CadSceneBuilder, SceneOptions};
#[allow(unused_imports)]
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
#[allow(unused_imports)]
use netdxf_shadow_rendering::{RenderCompileOptions, RenderCompiler, RenderScene};
#[allow(unused_imports)]
use netdxf_shadow_samples::StudioSamples;
#[allow(unused_imports)]
use netdxf_shadow_workflows::IWorkflowDriver;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use serde_json::Value;
#[allow(unused_imports)]
use std::collections::BTreeMap;
#[allow(unused_imports)]
use std::sync::Arc;

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadNewDocumentRequest {
    pub name: String,
    pub sample: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadCloseDocumentRequest {
    pub document_id: String,
}

/// Multi-document hub managing open [`Studio`] instances.
#[derive(Default)]
pub struct StudioDocumentHub {
    documents: BTreeMap<String, Studio>,
}

impl StudioDocumentHub {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn open_sample(&mut self, document_id: impl Into<String>, sample_name: &str) -> &mut Studio {
        let id = document_id.into();
        self.documents
            .entry(id.clone())
            .or_insert_with(|| Studio::from_sample(id, sample_name))
    }

    pub fn get(&self, document_id: &str) -> Option<&Studio> {
        self.documents.get(document_id)
    }

    pub fn get_mut(&mut self, document_id: &str) -> Option<&mut Studio> {
        self.documents.get_mut(document_id)
    }

    pub fn close(&mut self, document_id: &str) -> bool {
        self.documents.remove(document_id).is_some()
    }
}

