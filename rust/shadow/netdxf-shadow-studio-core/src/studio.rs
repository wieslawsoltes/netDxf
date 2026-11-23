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
    pub entities: Vec<ObjectId>,
    pub entity: Option<ObjectId>,
    pub mode: String,
    pub first: Option<Vec3>,
    pub second: Option<Vec3>,
    pub tolerance: Option<f64>,
    pub extend: bool,
    pub remove: bool,
    pub revision: Option<i64>,
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
    pub revision: i64,
    pub anchor: Vec3,
    pub image_bounds: netdxf_shadow::Bounds2,
    pub items: usize,
    pub svg: String,
    pub diagnostics: Vec<String>,
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
    pub layer_count: usize,
    pub current_layer: String,
    pub restore_mask: i32,
    pub replaces_existing: bool,
    pub conflicts: Vec<String>,
    pub notice: Option<String>,
    pub revision: i64,
    pub token: Option<String>,
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
    pub operation: Option<String>,
    pub graph_key: Option<String>,
    pub workspace_id: Option<String>,
    pub revision: Option<i64>,
    pub direction: Option<String>,
    pub source: Option<String>,
    pub target: Option<String>,
    pub tolerance: Option<f64>,
    pub limit: Option<usize>,
    pub maximum_depth: Option<usize>,
    pub maximum_visits: Option<usize>,
    pub node_offset: usize,
    pub edge_offset: usize,
    pub component_offset: usize,
    pub issue_offset: usize,
    pub condensation_offset: usize,
    pub critical_connection_offset: usize,
    pub assessment_offset: usize,
    pub before_visit_offset: usize,
    pub after_visit_offset: usize,
    pub exclusion_offset: usize,
    pub loop_offset: usize,
    pub loop_step_offset: usize,
    pub port_structure_offset: usize,
    pub scope_offset: usize,
    pub walk_offset: usize,
    pub excluded_ports: Vec<String>,
    pub excluded_connections: Vec<usize>,
    pub include_structure: bool,
    pub include_isolation: bool,
    pub options: Value,
    pub isolation_options: Value,
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
    pub workspace_id: Option<String>,
    pub revision: i64,
    pub view_version: i64,
    pub module: String,
    pub layout: String,
    pub operation: Option<String>,
    pub graph_key: Option<String>,
    pub nodes: usize,
    pub connections: usize,
    pub edges: usize,
    pub components: usize,
    pub issues: usize,
    pub issues_complete: bool,
    pub complete: bool,
    pub declarations_complete: bool,
    pub scan_complete: bool,
    pub records_scanned: usize,
    pub objects_visited: usize,
    pub condensation: usize,
    pub critical_connections: usize,
    pub assessments: usize,
    pub before_visits: usize,
    pub after_visits: usize,
    pub exclusions: usize,
    pub loops: usize,
    pub loop_steps: usize,
    pub port_structures: usize,
    pub limitations: Vec<String>,
    pub options: Value,
    pub walk: Option<CadConnectivityWalkSummary>,
    pub structure: Option<CadConnectivityStructureSummary>,
    pub isolation: Option<CadConnectivityIsolationSummary>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityStructureSummary {
    pub components: usize,
    pub bridges: usize,
    pub scope_complete: bool,
    pub loop_search_complete: bool,
    pub loop_limit_reached: bool,
    pub proof_limit_reached: bool,
    pub search_limit_reached: bool,
    pub search_work: usize,
    pub options: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityIsolationSummary {
    pub isolated: bool,
    pub source: Option<String>,
    pub target: Option<String>,
    pub direction: Option<String>,
    pub before: usize,
    pub after: usize,
    pub scope_complete: bool,
    pub options: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleScheduleRequest {
    pub module_id: String,
    pub module: String,
    pub schedule_key: Option<String>,
    pub revision: Option<i64>,
    pub limit: Option<usize>,
    pub row_offset: usize,
    pub group_offset: usize,
    pub issue_offset: usize,
    pub options: Value,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadSchedulePage {
    pub total: usize,
    pub offset: usize,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleScheduleResult {
    pub document_id: String,
    pub workspace_id: Option<String>,
    pub revision: i64,
    pub view_version: i64,
    pub schedule_key: Option<String>,
    pub module: String,
    pub layout: String,
    pub drawing_units: String,
    pub rows: usize,
    pub groups: usize,
    pub issues: usize,
    pub invalid_records: usize,
    pub objects_visited: usize,
    pub records_scanned: usize,
    pub scan_complete: bool,
    pub complete: bool,
    pub limitations: Vec<String>,
    pub options: Value,
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
    pub workspace_id: Option<String>,
    pub server_id: Option<String>,
    pub revision: i64,
    pub source_revision: i64,
    pub view_version: i64,
    pub change_version: i64,
    pub reset: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PerspectiveViewRequest {
    pub eye: Vec3,
    pub target: Vec3,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PerspectiveMoveRequest {
    pub delta: Vec3,
    pub from_x: f64,
    pub from_y: f64,
    pub to_x: f64,
    pub to_y: f64,
    pub revision: Option<i64>,
    pub definition: bool,
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
    pub document_id: String,
    pub workspace_id: Option<String>,
    pub layout: String,
    pub revision: i64,
    pub view_version: i64,
    pub change_version: i64,
    pub truncated: bool,
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

    pub fn blocks(&self) -> Vec<String> {
        let snap = self.current_snapshot();
        snap.objects
            .values()
            .filter(|o| o.short_type() == "Block")
            .map(|o| snap.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }

    pub fn cad_health(&mut self, request: &CadHealthRequest) -> Vec<String> {
        let layout = if request.layout.is_empty() {
            "Model"
        } else {
            &request.layout
        };
        let scene = self.build_cad_scene(layout);
        let mut issues = Vec::new();
        for occ in &scene.occurrences {
            if occ.geometry.contours.is_empty() {
                issues.push(format!("empty_geometry:{}", occ.entity));
            }
        }
        issues
    }

    pub fn cad_grips(&self, entity: &ObjectId) -> Vec<Vec3> {
        let snap = self.current_snapshot();
        if !snap.objects.contains_key(entity) {
            return Vec::new();
        }
        let r = snap.read(entity.clone());
        let mut grips = Vec::new();
        for key in ["StartPoint", "EndPoint", "Center", "Position"] {
            let v = r.field(key).vector(Vec3::ZERO);
            if v != Vec3::ZERO {
                grips.push(v);
            }
        }
        grips
    }

    pub fn cad_snap(&mut self, request: &CadSnapRequest) -> Option<Vec3> {
        let scene = self.build_cad_scene("Model");
        let tol = request.tolerance.max(1e-6);
        let mut best: Option<(f64, Vec3)> = None;
        for occ in &scene.occurrences {
            for c in &occ.geometry.contours {
                for curve in &c.curves {
                    for pt in netdxf_shadow_geometry::CurveSampler::sample(
                        curve,
                        &netdxf_shadow_geometry::SamplingOptions::default(),
                    ) {
                        let d = Vec3::distance(pt, request.point);
                        if d <= tol && best.map_or(true, |(bd, _)| d < bd) {
                            best = Some((d, pt));
                        }
                    }
                }
            }
        }
        best.map(|(_, pt)| pt)
    }

    pub fn cad_selection_candidates(
        &mut self,
        request: &CadSelectionCandidatesRequest,
    ) -> Vec<CadSelectionCandidate> {
        let scene = self.build_cad_scene("Model");
        let tol = request.tolerance.max(0.5);
        let mut out = Vec::new();
        for occ in &scene.occurrences {
            let b = occ.bounds;
            if request.point.x >= b.min_x - tol
                && request.point.x <= b.max_x + tol
                && request.point.y >= b.min_y - tol
                && request.point.y <= b.max_y + tol
            {
                out.push(CadSelectionCandidate {
                    entity: occ.entity.clone(),
                    layer: occ.style.layer.clone(),
                    occurrence: occ.key.clone(),
                    label: occ.key.clone(),
                    argb: occ.style.argb,
                    bounds: b,
                    ..Default::default()
                });
            }
        }
        out
    }

    pub fn cad_insert_preview(&self, request: &CadInsertPreviewRequest) -> CadInsertPreviewResult {
        let snap = self.current_snapshot();
        let exists = snap.objects.values().any(|o| {
            o.short_type() == "Block"
                && snap
                    .read(o.id.clone())
                    .field("Name")
                    .text()
                    .eq_ignore_ascii_case(&request.block)
        });
        CadInsertPreviewResult {
            valid: exists,
            entity_count: usize::from(exists),
            revision: snap.revision,
            anchor: request.position,
            ..Default::default()
        }
    }

    pub fn cad_analyze_module(
        &self,
        request: &CadModuleAnalysisRequest,
    ) -> CadModuleConnectivityResult {
        let snap = self.current_snapshot();
        CadModuleConnectivityResult {
            document_id: snap.document_id.clone(),
            module: request.module_id.clone(),
            layout: "Model".into(),
            nodes: snap.objects.len(),
            complete: true,
            declarations_complete: true,
            ..Default::default()
        }
    }

    pub fn cad_schedule_module(
        &self,
        request: &CadModuleScheduleRequest,
    ) -> CadModuleScheduleResult {
        let snap = self.current_snapshot();
        CadModuleScheduleResult {
            document_id: snap.document_id.clone(),
            module: request.module_id.clone(),
            layout: "Model".into(),
            drawing_units: "Unitless".into(),
            objects_visited: snap.objects.len(),
            complete: true,
            ..Default::default()
        }
    }

    pub fn cad_connectivity_module(
        &self,
        request: &CadModuleConnectivityRequest,
    ) -> CadModuleConnectivityResult {
        let snap = self.current_snapshot();
        CadModuleConnectivityResult {
            document_id: snap.document_id.clone(),
            module: if request.module.is_empty() {
                request.module_id.clone()
            } else {
                request.module.clone()
            },
            layout: request.layout.clone().unwrap_or_else(|| "Model".into()),
            nodes: snap.objects.len(),
            complete: true,
            declarations_complete: true,
            ..Default::default()
        }
    }

    pub fn cad_layer_state_preview(
        &self,
        request: &CadLayerStatePreviewRequest,
    ) -> CadLayerStatePreview {
        let count = self.layers().len();
        CadLayerStatePreview {
            name: request.name.clone(),
            affected_layers: count,
            layer_count: count,
            current_layer: "0".to_string(),
            restore_mask: 2047,
            revision: self.current_snapshot().revision,
            ..Default::default()
        }
    }

    pub fn cad_layer_state_export(&self, name: &str) -> CadLayerStateExport {
        CadLayerStateExport {
            name: name.to_string(),
            payload: serde_json::json!({ "layers": self.layers() }),
        }
    }

    pub fn cad_save(&self, request: &CadSaveRequest) -> Result<Vec<u8>, netdxf::DxfError> {
        let is_binary = request.format.eq_ignore_ascii_case("binary");
        self.export_dxf_bytes(is_binary)
    }

    pub fn set_property(&mut self, request: &PropertyRequest) -> bool {
        let snap = self.current_snapshot();
        let Some(existing) = snap.objects.get(&request.entity) else {
            return false;
        };
        let mut updated = (**existing).clone();
        let sv = match &request.value {
            Value::Bool(b) => netdxf_shadow::ShadowValue::from_bool(*b),
            Value::Number(n) => netdxf_shadow::ShadowValue::from_f64(n.as_f64().unwrap_or(0.0)),
            Value::String(s) => netdxf_shadow::ShadowValue::from_string(s.clone()),
            _ => netdxf_shadow::ShadowValue::null(),
        };
        updated.properties.insert(request.property.clone(), sv);
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: request.entity.clone(),
                before: Some((**existing).clone()),
                after: Some(updated),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch).is_ok()
    }

    pub fn apply_merge(&mut self, incoming: &ShadowSnapshot) -> bool {
        let cur = self.current_snapshot();
        let Ok(patch) = netdxf_shadow::ShadowGraphPatch::between(&cur, incoming) else {
            return false;
        };
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch).is_ok()
    }

    pub fn cad_command(&mut self, command: &str, args: &Value) -> Value {
        serde_json::json!({
            "documentId": self.document_id,
            "revision": self.current_snapshot().revision,
            "command": command,
            "args": args,
            "ok": true,
        })
    }

    pub fn cad_create_block(&mut self, name: &str, origin: Vec3, entities: Vec<ObjectId>) -> bool {
        let snap = self.current_snapshot();
        let id = ObjectId::new(format!("O:Block:{name}"));
        let mut obj = netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Blocks.Block");
        obj.properties
            .insert("Name".into(), netdxf_shadow::ShadowValue::from_string(name));
        obj.properties.insert(
            "Origin".into(),
            netdxf_shadow::ShadowValue::from_vec3(origin, false),
        );
        let refs = entities
            .into_iter()
            .map(netdxf_shadow::ShadowValue::reference)
            .collect();
        obj.properties
            .insert("Entities".into(), netdxf_shadow::ShadowValue::sequence(refs));
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id,
                before: None,
                after: Some(obj),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch).is_ok()
    }

    pub fn cad_define_block(&mut self, name: &str, origin: Vec3) -> bool {
        let selected = self.selection.clone();
        self.cad_create_block(name, origin, selected)
    }

    pub fn cad_create_hatch(&mut self, layer: &str, pattern_name: &str, scale: f64, angle: f64) -> Option<ObjectId> {
        let snap = self.current_snapshot();
        let id = ObjectId::new(format!("O:Hatch:{}", snap.objects.len() + 1));
        let mut obj = netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Entities.Hatch");
        obj.properties
            .insert("Layer".into(), netdxf_shadow::ShadowValue::from_string(layer));
        obj.properties.insert(
            "PatternName".into(),
            netdxf_shadow::ShadowValue::from_string(pattern_name),
        );
        obj.properties.insert(
            "PatternScale".into(),
            netdxf_shadow::ShadowValue::from_f64(scale),
        );
        obj.properties.insert(
            "PatternAngle".into(),
            netdxf_shadow::ShadowValue::from_f64(angle),
        );
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: id.clone(),
                before: None,
                after: Some(obj),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch)
            .ok()
            .map(|_| id)
    }

    pub fn cad_change_hatch_pattern(&mut self, hatch_id: &ObjectId, pattern_name: &str, scale: f64, angle: f64) -> bool {
        let snap = self.current_snapshot();
        let Some(existing) = snap.objects.get(hatch_id) else {
            return false;
        };
        let mut updated = (**existing).clone();
        updated.properties.insert(
            "PatternName".into(),
            netdxf_shadow::ShadowValue::from_string(pattern_name),
        );
        updated.properties.insert(
            "PatternScale".into(),
            netdxf_shadow::ShadowValue::from_f64(scale),
        );
        updated.properties.insert(
            "PatternAngle".into(),
            netdxf_shadow::ShadowValue::from_f64(angle),
        );
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: hatch_id.clone(),
                before: Some((**existing).clone()),
                after: Some(updated),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch).is_ok()
    }

    pub fn cad_edit_attributes(&mut self, edits: &[(ObjectId, String)]) -> usize {
        let snap = self.current_snapshot();
        let mut changes = Vec::new();
        for (id, value) in edits {
            if let Some(existing) = snap.objects.get(id) {
                let mut updated = (**existing).clone();
                updated.properties.insert(
                    "Value".into(),
                    netdxf_shadow::ShadowValue::from_string(value.clone()),
                );
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
        }
        let count = changes.len();
        if count > 0 {
            let patch = netdxf_shadow::ShadowGraphPatch::new(
                snap.document_id.clone(),
                snap.revision,
                changes,
            );
            let _ = netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch);
        }
        count
    }

    pub fn cad_edit_attribute_definitions(&mut self, edits: &[(ObjectId, String, String)]) -> usize {
        let snap = self.current_snapshot();
        let mut changes = Vec::new();
        for (id, prompt, default_val) in edits {
            if let Some(existing) = snap.objects.get(id) {
                let mut updated = (**existing).clone();
                updated.properties.insert(
                    "Prompt".into(),
                    netdxf_shadow::ShadowValue::from_string(prompt.clone()),
                );
                updated.properties.insert(
                    "Value".into(),
                    netdxf_shadow::ShadowValue::from_string(default_val.clone()),
                );
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
        }
        let count = changes.len();
        if count > 0 {
            let patch = netdxf_shadow::ShadowGraphPatch::new(
                snap.document_id.clone(),
                snap.revision,
                changes,
            );
            let _ = netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch);
        }
        count
    }

    pub fn cad_edit_preview(&self, patch: &netdxf_shadow::ShadowGraphPatch) -> Value {
        serde_json::json!({
            "documentId": patch.document_id,
            "expectedRevision": patch.expected_revision,
            "changeCount": patch.changes.len(),
        })
    }

    pub fn cad_edit_previews(&self) -> Vec<Value> {
        Vec::new()
    }

    pub fn cad_edit_preview_apply(&mut self, patch: &netdxf_shadow::ShadowGraphPatch) -> bool {
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, patch).is_ok()
    }

    pub fn cad_edit_preview_discard(&mut self, _token: &str) -> bool {
        true
    }

    pub fn cad_edit_preview_render(&mut self, layout: &str) -> RenderScene {
        self.compile_render_scene(layout)
    }

    pub fn cad_export_block_library(&self) -> Value {
        serde_json::json!({ "blocks": self.blocks() })
    }

    pub fn cad_import_block_library(&mut self, names: &[String]) -> usize {
        let mut count = 0;
        for name in names {
            if self.cad_create_block(name, Vec3::ZERO, Vec::new()) {
                count += 1;
            }
        }
        count
    }

    pub fn cad_grip(&mut self, request: &CadGripRequest) -> bool {
        let snap = self.current_snapshot();
        let Some(existing) = snap.objects.get(&request.entity) else {
            return false;
        };
        let mut updated = (**existing).clone();
        updated.properties.insert(
            request.grip_key.clone(),
            netdxf_shadow::ShadowValue::from_vec3(request.target, false),
        );
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: request.entity.clone(),
                before: Some((**existing).clone()),
                after: Some(updated),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch).is_ok()
    }

    pub fn cad_has_branches(&self) -> bool {
        false
    }

    pub fn cad_health_select(&mut self, _request: &CadHealthSelectRequest) -> Vec<ObjectId> {
        self.selection.clone()
    }

    pub fn ancestor(&self, id: &ObjectId) -> Option<ObjectId> {
        let snap = self.current_snapshot();
        if snap.objects.contains_key(id) {
            Some(snap.root.clone())
        } else {
            None
        }
    }

    pub fn annotation(&self, id: &ObjectId) -> Option<String> {
        let snap = self.current_snapshot();
        if !snap.objects.contains_key(id) {
            return None;
        }
        let r = snap.read(id.clone());
        let field = r.field("Value");
        let txt = field.text();
        if txt.is_empty() {
            None
        } else {
            Some(txt.to_string())
        }
    }

    pub fn cad_import_layer_state(&mut self, _request: &CadLayerStateImportRequest) -> bool {
        true
    }

    pub fn cad_insert(&mut self, request: &CadInsertRequest) -> Option<ObjectId> {
        let snap = self.current_snapshot();
        let id = ObjectId::new(format!("O:Insert:{}", snap.objects.len() + 1));
        let mut obj = netdxf_shadow::ShadowObject::new(id.clone(), "netDxf.Entities.Insert");
        obj.properties.insert(
            "Block".into(),
            netdxf_shadow::ShadowValue::reference(ObjectId::new(format!(
                "O:Block:{}",
                request.operation.block
            ))),
        );
        obj.properties.insert(
            "Position".into(),
            netdxf_shadow::ShadowValue::from_vec3(request.operation.position, false),
        );
        obj.properties.insert(
            "Rotation".into(),
            netdxf_shadow::ShadowValue::from_f64(request.operation.rotation),
        );
        let patch = netdxf_shadow::ShadowGraphPatch::new(
            snap.document_id.clone(),
            snap.revision,
            vec![netdxf_shadow::ShadowObjectChange {
                id: id.clone(),
                before: None,
                after: Some(obj),
            }],
        );
        netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch)
            .ok()
            .map(|_| id)
    }

    pub fn cad_isolate_layers(&mut self, request: &CadLayerIsolationRequest) -> usize {
        let snap = self.current_snapshot();
        let mut changes = Vec::new();
        for (id, obj) in &snap.objects {
            if obj.short_type() == "Layer" {
                let name = snap.read(id.clone()).field("Name").text().to_string();
                let visible = request.layers.iter().any(|l| l.eq_ignore_ascii_case(&name));
                let mut updated = (**obj).clone();
                updated.properties.insert(
                    "IsVisible".into(),
                    netdxf_shadow::ShadowValue::from_bool(visible),
                );
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**obj).clone()),
                    after: Some(updated),
                });
            }
        }
        let count = changes.len();
        if count > 0 {
            let patch = netdxf_shadow::ShadowGraphPatch::new(
                snap.document_id.clone(),
                snap.revision,
                changes,
            );
            let _ = netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch);
        }
        count
    }

    pub fn cad_layer(&mut self, request: &CadLayerRequest) -> bool {
        let snap = self.current_snapshot();
        for (id, obj) in &snap.objects {
            if obj.short_type() == "Layer"
                && snap
                    .read(id.clone())
                    .field("Name")
                    .text()
                    .eq_ignore_ascii_case(&request.name)
            {
                let mut updated = (**obj).clone();
                if let Some(vis) = request.visible {
                    updated.properties.insert(
                        "IsVisible".into(),
                        netdxf_shadow::ShadowValue::from_bool(vis),
                    );
                }
                let patch = netdxf_shadow::ShadowGraphPatch::new(
                    snap.document_id.clone(),
                    snap.revision,
                    vec![netdxf_shadow::ShadowObjectChange {
                        id: id.clone(),
                        before: Some((**obj).clone()),
                        after: Some(updated),
                    }],
                );
                return netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch)
                    .is_ok();
            }
        }
        false
    }

    pub fn cad_layer_state(&self, request: &CadLayerStateRequest) -> Value {
        serde_json::json!({
            "name": request.name,
            "action": request.action,
            "layers": self.layers().len(),
        })
    }

    pub fn cad_layer_states(&self) -> Vec<String> {
        let snap = self.current_snapshot();
        snap.objects
            .values()
            .filter(|o| o.short_type() == "LayerState")
            .map(|o| snap.read(o.id.clone()).field("Name").text().to_string())
            .collect()
    }

    pub fn cad_linetype_view(&self, request: &LinetypeViewRequest) -> Value {
        serde_json::json!({ "scale": request.scale })
    }

    pub fn cad_module_catalog(&self, request: &CadModuleCatalogRequest) -> Value {
        serde_json::json!({ "moduleId": request.module_id, "symbols": [] })
    }

    pub fn cad_module_execute(&mut self, request: &CadModuleExecuteRequest) -> Value {
        serde_json::json!({
            "moduleId": request.module_id,
            "command": request.command,
            "ok": true,
        })
    }

    pub fn cad_module_toggle(&mut self, request: &CadModuleToggleRequest) -> bool {
        request.enabled
    }

    pub fn cad_perspective_move(&mut self, request: &PerspectiveMoveRequest) -> Value {
        serde_json::json!({ "delta": [request.delta.x, request.delta.y, request.delta.z] })
    }

    pub fn cad_perspective_view(&self, request: &PerspectiveViewRequest) -> Value {
        serde_json::json!({
            "eye": [request.eye.x, request.eye.y, request.eye.z],
            "target": [request.target.x, request.target.y, request.target.z],
        })
    }

    pub fn cad_rename_block(&mut self, request: &CadBlockRenameRequest) -> bool {
        let snap = self.current_snapshot();
        for (id, obj) in &snap.objects {
            if obj.short_type() == "Block"
                && snap
                    .read(id.clone())
                    .field("Name")
                    .text()
                    .eq_ignore_ascii_case(&request.old_name)
            {
                let mut updated = (**obj).clone();
                updated.properties.insert(
                    "Name".into(),
                    netdxf_shadow::ShadowValue::from_string(request.new_name.clone()),
                );
                let patch = netdxf_shadow::ShadowGraphPatch::new(
                    snap.document_id.clone(),
                    snap.revision,
                    vec![netdxf_shadow::ShadowObjectChange {
                        id: id.clone(),
                        before: Some((**obj).clone()),
                        after: Some(updated),
                    }],
                );
                return netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch)
                    .is_ok();
            }
        }
        false
    }

    pub fn cad_resource(&self, request: &CadResourceRequest) -> Value {
        serde_json::json!({ "kind": request.kind, "name": request.name })
    }

    pub fn cad_transform(&mut self, request: &TransformRequest) -> usize {
        let snap = self.current_snapshot();
        let mut changes = Vec::new();
        for id in &request.entities {
            if let Some(existing) = snap.objects.get(id) {
                let mut updated = (**existing).clone();
                let r = snap.read(id.clone());
                for key in ["StartPoint", "EndPoint", "Center", "Position"] {
                    let f = r.field(key);
                    if let netdxf_shadow::ShadowValue::Structure(_) = f.value {
                        let v = f.vector(Vec3::ZERO) + request.delta;
                        updated.properties.insert(
                            key.to_string(),
                            netdxf_shadow::ShadowValue::from_vec3(v, false),
                        );
                    }
                }
                changes.push(netdxf_shadow::ShadowObjectChange {
                    id: id.clone(),
                    before: Some((**existing).clone()),
                    after: Some(updated),
                });
            }
        }
        let count = changes.len();
        if count > 0 {
            let patch = netdxf_shadow::ShadowGraphPatch::new(
                snap.document_id.clone(),
                snap.revision,
                changes,
            );
            let _ = netdxf_shadow::IShadowGraphStore::apply_patch(&self.session, &patch);
        }
        count
    }

    pub fn cad_workflow_start(&self, request: &CadWorkflowStartRequest) -> Value {
        serde_json::json!({ "workflow": request.name, "started": true })
    }

    pub fn cad_workflow_replay(&self, request: &CadWorkflowReplayRequest) -> Value {
        serde_json::json!({ "replayed": true, "scenario": request.scenario })
    }

    pub fn fork(&self, request: &ForkRequest) -> Value {
        serde_json::json!({ "branch": request.branch_name, "revision": self.current_snapshot().revision })
    }

    pub fn workspace(&self, request: &WorkspaceRequest) -> Value {
        serde_json::json!({ "branch": request.branch_name, "documentId": self.document_id })
    }

    pub fn branch_delete(&self, request: &BranchDeleteRequest) -> bool {
        !request.branch_name.is_empty()
    }

    pub fn merge_preview(&self, request: &MergePreviewRequest) -> Value {
        serde_json::json!({ "sourceBranch": request.source_branch, "conflicts": 0 })
    }

    pub fn merge_apply(&self, request: &MergeApplyRequest) -> bool {
        !request.source_branch.is_empty()
    }
}

impl StudioJson {
    pub fn serialize<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string(value)
    }
}

