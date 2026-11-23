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
        CadLayerStatePreview {
            name: request.name.clone(),
            affected_layers: self.layers().len(),
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
}

impl StudioJson {
    pub fn serialize<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string(value)
    }
}

