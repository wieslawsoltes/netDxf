//! Headless CAD Studio hub, document sessions, and request/response contracts (`netDxf.Shadow.Studio.Core`).

#![forbid(unsafe_code)]

use netdxf_shadow::{ObjectId, ShadowSnapshot, Vec3};
use netdxf_shadow_geometry::{CadScene, CadSceneBuilder, SceneOptions};
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
use netdxf_shadow_rendering::{RenderCompileOptions, RenderCompiler, RenderScene};
use netdxf_shadow_samples::StudioSamples;
use netdxf_shadow_workflows::IWorkflowDriver;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::BTreeMap;
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
    pub synced: usize,
}
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadEditOperation {
    pub entity: ObjectId,
    pub property: String,
    pub value: Value,
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
}
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityPage {
    pub total: usize,
    pub offset: usize,
}
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadConnectivityWalkSummary {
    pub visited: usize,
}
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadModuleConnectivityResult {
    pub nodes: usize,
    pub connections: usize,
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
    pub rows: usize,
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
    pub label: String,
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
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct Route {
    pub method: String,
    pub path: String,
    pub description: String,
}
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadNewDocumentRequest {
    pub name: String,
    pub sample: Option<String>,
}
#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadCloseDocumentRequest {
    pub document_id: String,
}

pub struct StudioJson;
pub struct StudioAgentRouteTools;
pub struct StudioAgentSchema;
pub struct StudioSubscription {
    pub id: u64,
}

/// Interactive CAD Studio controller for a single document.
pub struct Studio {
    pub document_id: String,
    pub session: ShadowSession,
    pub scene_builder: CadSceneBuilder,
    pub render_compiler: RenderCompiler,
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
        }
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

/// Workflow replay driver executing operations against a [`Studio`] instance.
pub struct StudioWorkflowDriver {
    pub studio: Studio,
}

impl IWorkflowDriver for StudioWorkflowDriver {
    fn execute(&mut self, operation: &str, _arguments: &Value) -> Result<Value, String> {
        match operation {
            "snapshot.revision" => Ok(serde_json::json!({
                "revision": self.studio.current_snapshot().revision
            })),
            _ => Ok(serde_json::json!({ "ok": true, "operation": operation })),
        }
    }
}

impl StudioJson {
    pub fn serialize<T: Serialize>(value: &T) -> Result<String, serde_json::Error> {
        serde_json::to_string(value)
    }
}

impl StudioAgentRouteTools {
    pub fn routes() -> Vec<Route> {
        vec![
            Route {
                method: "GET".into(),
                path: "/api/cad/snapshot".into(),
                description: "Read current shadow snapshot summary".into(),
            },
            Route {
                method: "POST".into(),
                path: "/api/cad/pick".into(),
                description: "Hit-test entities near a world point".into(),
            },
        ]
    }
}

impl StudioAgentSchema {
    pub fn route_schema() -> Value {
        serde_json::json!({
            "type": "object",
            "routes": StudioAgentRouteTools::routes(),
        })
    }
}
