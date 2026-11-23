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
    pub demo: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadCloseDocumentRequest {
    pub document_id: String,
    pub revision: Option<i64>,
    pub discard: bool,
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

    pub fn create(&mut self, request: &CadNewDocumentRequest) -> &mut Studio {
        let sample = request.sample.as_deref().unwrap_or("line");
        self.open_sample(request.name.clone(), sample)
    }

    pub fn load(
        &mut self,
        document_id: impl Into<String>,
        bytes: &[u8],
    ) -> Result<&mut Studio, netdxf::DxfError> {
        let id = document_id.into();
        let studio = Studio::from_dxf_bytes(id.clone(), bytes)?;
        self.documents.insert(id.clone(), studio);
        Ok(self.documents.get_mut(&id).expect("inserted"))
    }

    pub fn save(&self, document_id: &str, is_binary: bool) -> Result<Vec<u8>, String> {
        let studio = self
            .documents
            .get(document_id)
            .ok_or_else(|| format!("Unknown document '{document_id}'."))?;
        studio
            .export_dxf_bytes(is_binary)
            .map_err(|e| e.to_string())
    }

    pub fn documents(&self) -> Vec<String> {
        self.documents.keys().cloned().collect()
    }

    pub fn studio(&self, document_id: &str) -> Option<&Studio> {
        self.documents.get(document_id)
    }

    pub fn owned(&self, document_id: &str) -> bool {
        self.documents.contains_key(document_id)
    }

    pub fn name(&self, document_id: &str) -> Option<&str> {
        self.documents.get(document_id).map(|s| s.document_id.as_str())
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

