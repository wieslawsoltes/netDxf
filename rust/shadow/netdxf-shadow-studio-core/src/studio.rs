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
pub struct AnnotationViewRequest {
    pub mode: String,
    pub scale: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadAttributeDefinitionOperation {
    pub action: String,
    pub tag: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadAttributeDefinitionRequest {
    pub block: String,
    pub operations: Vec<CadAttributeDefinitionOperation>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadAttributeRequest {
    pub entity: ObjectId,
    pub tag: String,
    pub value: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockCreateRequest {
    pub name: String,
    pub origin: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockBeginRequest {
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockFinishRequest {
    pub commit: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockLibraryOperation {
    pub action: String,
    pub block: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockLibraryExportRequest {
    pub blocks: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockLibrarySummary {
    pub block_count: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadCommandRequest {
    pub command: String,
    pub arguments: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSelectionRequest {
    pub ids: Vec<ObjectId>,
    pub mode: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadGripRequest {
    pub entity: ObjectId,
    pub grip_key: String,
    pub target: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerRequest {
    pub name: String,
    pub visible: Option<bool>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadAssignLayerRequest {
    pub entities: Vec<ObjectId>,
    pub layer: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSnapRequest {
    pub point: Vec3,
    pub tolerance: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockDefinitionRequest {
    pub name: String,
    pub origin: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadBlockRenameRequest {
    pub old_name: String,
    pub new_name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadHatchAuthoringRequest {
    pub layer: String,
    pub pattern: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadHatchPatternRequest {
    pub pattern: String,
    pub scale: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct StudioEditPreviewToolRequest {
    pub operation: String,
    pub arguments: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct StudioApplyEditPreviewToolRequest {
    pub token: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadPreviewProperty {
    pub name: String,
    pub before: String,
    pub after: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadAttributeSyncOperation {
    pub insert_id: ObjectId,
    pub tag: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadAttributeSyncSummary {
    pub block: Option<ObjectId>,
    pub block_name: String,
    pub synced: usize,
    pub added: usize,
    pub removed: usize,
    pub retained: usize,
    pub removed_x_data: usize,
    pub text_conversions: usize,
    pub reference_count: usize,
    pub references: Vec<ObjectId>,
    pub details_truncated: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadEditOperation {
    pub kind: String,
    pub entity: ObjectId,
    pub property: String,
    pub value: Value,
    pub command: Option<CadCommandRequest>,
    pub layer: Option<CadLayerRequest>,
    pub insert: Option<CadInsertOperation>,
    pub resource: Option<CadResourceRequest>,
    pub block_library: Option<CadBlockLibraryOperation>,
    pub attribute_definitions: Option<CadAttributeDefinitionRequest>,
    pub attribute_sync: Option<CadAttributeSyncOperation>,
    pub module: Option<String>,
    pub module_command: Option<CadModuleExecuteRequest>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadEditPreviewRequest {
    pub operations: Vec<CadEditOperation>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadEditPreviewTokenRequest {
    pub token: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct GraphQueryRequest {
    pub root: Option<ObjectId>,
    pub max_depth: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadHealthRequest {
    pub layout: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadHealthSelectRequest {
    pub issue_id: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadInsertPreviewRequest {
    pub block: String,
    pub position: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadInsertPreviewResult {
    pub valid: bool,
    pub entity_count: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadInsertOperation {
    pub block: String,
    pub position: Vec3,
    pub rotation: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadInsertRequest {
    pub operation: CadInsertOperation,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerIsolationRequest {
    pub layers: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerStatePreviewRequest {
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerStateImportRequest {
    pub payload: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerStateExport {
    pub name: String,
    pub payload: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerStatePreview {
    pub name: String,
    pub affected_layers: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct LinetypeViewRequest {
    pub scale: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleConnectivityRequest {
    pub module_id: String,
    pub module: String,
    pub layout: Option<String>,
    pub direction: Option<String>,
    pub source: Option<String>,
    pub target: Option<String>,
    pub tolerance: Option<f64>,
    pub node_offset: usize,
    pub edge_offset: usize,
    pub component_offset: usize,
    pub issue_offset: usize,
    pub condensation_offset: usize,
    pub critical_connection_offset: usize,
    pub assessment_offset: usize,
    pub before_visit_offset: usize,
    pub after_visit_offset: usize,
    pub excluded_ports: Vec<String>,
    pub excluded_connections: Vec<usize>,
    pub include_structure: bool,
    pub include_isolation: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityPage {
    pub total: usize,
    pub offset: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityWalkSummary {
    pub source: String,
    pub target: Option<String>,
    pub direction: String,
    pub status: String,
    pub visited: usize,
    pub examined_edges: usize,
    pub path_length: Option<usize>,
    pub complete: bool,
    pub scope_complete: bool,
    pub depth_limit_reached: bool,
    pub visit_limit_reached: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleConnectivityResult {
    pub document_id: String,
    pub module: String,
    pub layout: String,
    pub nodes: usize,
    pub connections: usize,
    pub edges: usize,
    pub components: usize,
    pub issues: usize,
    pub complete: bool,
    pub declarations_complete: bool,
    pub condensation: usize,
    pub critical_connections: usize,
    pub assessments: usize,
    pub before_visits: usize,
    pub after_visits: usize,
    pub walk: Option<CadConnectivityWalkSummary>,
    pub structure: Option<CadConnectivityStructureSummary>,
    pub isolation: Option<CadConnectivityIsolationSummary>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityStructureSummary {
    pub components: usize,
    pub bridges: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityIsolationSummary {
    pub isolated: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleScheduleRequest {
    pub module_id: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSchedulePage {
    pub total: usize,
    pub offset: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleScheduleResult {
    pub document_id: String,
    pub module: String,
    pub layout: String,
    pub drawing_units: String,
    pub rows: usize,
    pub groups: usize,
    pub issues: usize,
    pub invalid_records: usize,
    pub objects_visited: usize,
    pub complete: bool,
    pub limitations: Vec<String>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleToggleRequest {
    pub module_id: String,
    pub enabled: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleCatalogRequest {
    pub module_id: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleAnalysisRequest {
    pub module_id: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleExecuteRequest {
    pub module_id: String,
    pub command: String,
    pub arguments: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct StudioRevisionNotice {
    pub document_id: String,
    pub revision: i64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PerspectiveViewRequest {
    pub eye: Vec3,
    pub target: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PerspectiveMoveRequest {
    pub delta: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadResourceRequest {
    pub kind: String,
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLayerStateRequest {
    pub name: String,
    pub action: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSaveRequest {
    pub format: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSelectionCandidatesRequest {
    pub point: Vec3,
    pub tolerance: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSelectionPreviewPath {
    pub points: Vec<Vec3>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSelectionCandidate {
    pub entity: ObjectId,
    pub leaf_entity: Option<ObjectId>,
    pub handle: String,
    pub r#type: String,
    pub leaf_type: Option<String>,
    pub layer: String,
    pub occurrence: String,
    pub label: String,
    pub argb: u32,
    pub bounds: netdxf_shadow::Bounds2,
    pub preview: Vec<CadSelectionPreviewPath>,
    pub preview_truncated: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSelectionCandidatesResult {
    pub candidates: Vec<CadSelectionCandidate>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct TransformRequest {
    pub entities: Vec<ObjectId>,
    pub delta: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadWorkflowStartRequest {
    pub name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadWorkflowReplayRequest {
    pub scenario: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ForkRequest {
    pub branch_name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct WorkspaceRequest {
    pub branch_name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct BranchDeleteRequest {
    pub branch_name: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct MergePreviewRequest {
    pub source_branch: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct MergeApplyRequest {
    pub source_branch: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct StudioRequestIdentity {
    pub document_id: String,
    pub branch: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RevisionRequest {
    pub expected_revision: i64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PickRequest {
    pub point: Vec3,
    pub tolerance: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct SelectRequest {
    pub ids: Vec<ObjectId>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct MoveRequest {
    pub ids: Vec<ObjectId>,
    pub delta: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PropertyRequest {
    pub entity: ObjectId,
    pub property: String,
    pub value: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct LayoutRequest {
    pub layout: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct AddRequest {
    pub entity_type: String,
    pub properties: Value,
}

pub struct StudioJson;

pub struct StudioSubscription {
    pub id: u64,
}

/// Interactive CAD Studio controller for a single document.
pub struct Studio {
    pub document_id: String,
    pub session: ShadowSession,
    pub scene_builder: CadSceneBuilder,
    pub render_compiler: RenderCompiler,
    pub selection: Vec<ObjectId>,
    pub active_block: Option<String>,
    pub subscriptions: Vec<u64>,
    next_subscription_id: u64,
}

impl Studio {
    pub fn from_sample(document_id: impl Into<String>, sample_name: &str) -> Self {
        let doc_id = document_id.into();
        let doc = StudioSamples::create(sample_name);
        let session =
            ShadowSession::from_document(&doc, doc_id.clone(), ShadowCaptureOptions::default());
        Self {
            document_id: doc_id,
            session,
            scene_builder: CadSceneBuilder::new(),
            render_compiler: RenderCompiler::new(),
            selection: Vec::new(),
            active_block: None,
            subscriptions: Vec::new(),
            next_subscription_id: 1,
        }
    }

    pub fn from_dxf_bytes(
        document_id: impl Into<String>,
        bytes: &[u8],
    ) -> Result<Self, netdxf::DxfError> {
        let doc_id = document_id.into();
        let doc = netdxf::DxfDocument::load_from_bytes(bytes)?;
        let session =
            ShadowSession::from_document(&doc, doc_id.clone(), ShadowCaptureOptions::default());
        Ok(Self {
            document_id: doc_id,
            session,
            scene_builder: CadSceneBuilder::new(),
            render_compiler: RenderCompiler::new(),
            selection: Vec::new(),
            active_block: None,
            subscriptions: Vec::new(),
            next_subscription_id: 1,
        })
    }

    pub fn export_dxf_bytes(&self, is_binary: bool) -> Result<Vec<u8>, netdxf::DxfError> {
        let mut doc = netdxf::DxfDocument::new();
        self.session.write_back(&mut doc);
        doc.save_to_bytes(is_binary)
    }

    pub fn current_snapshot(&self) -> Arc<ShadowSnapshot> {
        netdxf_shadow::IShadowStore::current(&self.session)
    }

    pub fn build_cad_scene(&mut self, layout: &str) -> CadScene {
        let snap = self.current_snapshot();
        self.scene_builder.build(
            &snap,
            &SceneOptions {
                layout: layout.into(),
                ..Default::default()
            },
        )
    }

    pub fn compile_render_scene(&mut self, layout: &str) -> RenderScene {
        let cad = self.build_cad_scene(layout);
        self.render_compiler
            .compile(&cad, &RenderCompileOptions::default())
    }

    pub fn active_subscriptions(&self) -> usize {
        self.subscriptions.len()
    }

    pub fn subscribe(&mut self) -> StudioSubscription {
        let id = self.next_subscription_id;
        self.next_subscription_id += 1;
        self.subscriptions.push(id);
        StudioSubscription { id }
    }

    pub fn unsubscribe(&mut self, sub: &StudioSubscription) -> bool {
        if let Some(idx) = self.subscriptions.iter().position(|&x| x == sub.id) {
            self.subscriptions.remove(idx);
            true
        } else {
            false
        }
    }

    pub fn select(&mut self, request: &CadSelectionRequest) -> Vec<ObjectId> {
        match request.mode.as_str() {
            "add" => {
                for id in &request.ids {
                    if !self.selection.contains(id) {
                        self.selection.push(id.clone());
                    }
                }
            }
            "remove" => {
                self.selection.retain(|id| !request.ids.contains(id));
            }
            "clear" => self.selection.clear(),
            _ => self.selection = request.ids.clone(),
        }
        self.selection.clone()
    }

    pub fn cad_begin_block(&mut self, request: &CadBlockBeginRequest) {
        self.active_block = Some(request.name.clone());
    }

    pub fn cad_finish_block(&mut self, _request: &CadBlockFinishRequest) -> Option<String> {
        self.active_block.take()
    }

    pub fn block_root(&self) -> Option<&str> {
        self.active_block.as_deref()
    }

    pub fn cad_block_libraries(&self) -> CadBlockLibrarySummary {
        let snap = self.current_snapshot();
        let count = snap
            .objects
            .values()
            .filter(|o| o.short_type() == "Block")
            .count();
        CadBlockLibrarySummary { block_count: count }
    }

    pub fn cad_assign_layer(&mut self, request: &CadAssignLayerRequest) -> usize {
        let snap = self.current_snapshot();
        let mut changes = Vec::new();
        for id in &request.entities {
            if let Some(existing) = snap.objects.get(id) {
                let mut updated = (**existing).clone();
                updated.properties.insert(
                    "Layer".into(),
                    netdxf_shadow::ShadowValue::from_string(request.layer.clone()),
                );
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
        }
        let count = changes.len();
        if !changes.is_empty() {
            let patch = netdxf_shadow::ShadowGraphPatch::new(
                snap.document_id.clone(),
                snap.revision,
                changes,
            );
            let _ = netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch);
        }
        count
    }

    pub fn add(&mut self, request: &AddRequest) -> Option<ObjectId> {
        let snap = self.current_snapshot();
        let new_id = ObjectId::new(format!("E_{}", snap.objects.len() + 1));
        let mut obj = netdxf_shadow::ShadowObject::new(new_id.clone(), request.entity_type.clone());
        if let Some(map) = request.properties.as_object() {
            for (k, v) in map {
                let sv = match v {
                    Value::Bool(b) => netdxf_shadow::ShadowValue::from_bool(*b),
                    Value::Number(n) => {
                        netdxf_shadow::ShadowValue::from_f64(n.as_f64().unwrap_or(0.0))
                    }
                    Value::String(s) => netdxf_shadow::ShadowValue::from_string(s.clone()),
                    _ => netdxf_shadow::ShadowValue::null(),
                };
                obj.properties.insert(k.clone(), sv);
            }
        }
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: new_id.clone(),
                before: None,
                after: Some(obj),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch).ok()?;
        Some(new_id)
    }

    pub fn layers(&self) -> Vec<String> {
        let snap = self.current_snapshot();
        snap.objects
            .values()
            .filter(|o| o.short_type() == "Layer")
            .map(|o| snap.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }

    pub fn layouts(&self) -> Vec<String> {
        let snap = self.current_snapshot();
        snap.objects
            .values()
            .filter(|o| o.short_type() == "Layout")
            .map(|o| snap.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }
}

impl StudioJson {
    pub fn serialize<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string(value)
    }
}

