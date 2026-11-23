use netdxf_shadow::{ObjectId, ShadowMemoryStore, ShadowValue, Vec3};
use netdxf_shadow_agents::{AgentFile, MemoryAgentWorkspace};
use netdxf_shadow_agents_evaluation::{
    BudgetChatClient, BudgetLedger, BudgetRequest, EvaluationBudgetOptions,
};
use netdxf_shadow_agents_hosting::BrowserStudioRelay;
use netdxf_shadow_editor::{EditorSession, ShadowPreviewOptions};
use netdxf_shadow_graph::{
    GraphEdge, GraphNode, GraphVisualizationOptions, TopologyGraph,
};
use netdxf_shadow_rendering::{
    ITextLayoutEngine, PerspectiveCamera, PerspectiveOptions, RenderFrame, RichTextStyle, Viewport,
};
use netdxf_shadow_rendering_skia::{SkiaRenderBackend, SkiaTextLayoutEngine};
use netdxf_shadow_studio_agents::{StudioAgentConfiguration, StudioAgentController};
use netdxf_shadow_studio_core::{AddRequest, CadAssignLayerRequest, Studio};
use netdxf_shadow_symbols::{
    DomainConnectivity, DomainConnectivityOptions, DomainPortNode, DomainSchedule,
    DomainScheduleOptions, DomainScheduleRow,
};
use std::collections::BTreeMap;

#[test]
fn shadow_graph_tarjan_bridges_and_articulation_points() {
    // 0 -- 1 -- 2 -- 3 (chain: 1 and 2 are articulation points, all 3 edges are bridges)
    let g = TopologyGraph {
        document_id: "doc".into(),
        revision: 1,
        nodes: vec![
            GraphNode { id: 0, position: Vec3::xy(0.0, 0.0), edges: vec![0] },
            GraphNode { id: 1, position: Vec3::xy(1.0, 0.0), edges: vec![0, 1] },
            GraphNode { id: 2, position: Vec3::xy(2.0, 0.0), edges: vec![1, 2] },
            GraphNode { id: 3, position: Vec3::xy(3.0, 0.0), edges: vec![2] },
        ],
        edges: vec![
            GraphEdge { id: 0, a: 0, b: 1, length: 1.0, entity: ObjectId::new("L0"), occurrence: "0".into() },
            GraphEdge { id: 1, a: 1, b: 2, length: 1.0, entity: ObjectId::new("L1"), occurrence: "1".into() },
            GraphEdge { id: 2, a: 2, b: 3, length: 1.0, entity: ObjectId::new("L2"), occurrence: "2".into() },
        ],
    };
    assert_eq!(g.articulation_points(), vec![1, 2]);
    assert_eq!(g.bridges(), vec![0, 1, 2]);
    assert_eq!(g.dead_ends(), vec![0, 3]);
    assert!((g.length() - 3.0).abs() < 1e-12);
    let vis = g.visualize(&GraphVisualizationOptions::default());
    assert_eq!(vis.articulation_point_count, 2);
    assert_eq!(vis.bridge_count, 3);
    assert_eq!(vis.dead_end_count, 2);
}

#[test]
fn shadow_camera_skia_svg_and_text_cache_parity() {
    let cam = PerspectiveCamera {
        eye: Vec3::new(0.0, 0.0, 10.0),
        target: Vec3::ZERO,
        up: Vec3::UNIT_Y,
        options: PerspectiveOptions::default(),
    };
    let proj = cam.try_project(Vec3::ZERO, 800.0, 600.0).expect("visible");
    assert!((proj.x - 400.0).abs() < 1e-6);
    assert!((proj.y - 300.0).abs() < 1e-6);
    let unproj = cam.unproject(400.0, 300.0, 800.0, 600.0).expect("hits plane");
    assert!(unproj.length() < 1e-6);

    let engine = SkiaTextLayoutEngine::new();
    let style = RichTextStyle::default();
    let m1 = engine.measure_run("Hello CAD", &style);
    let m2 = engine.measure_run("Hello CAD", &style);
    assert_eq!(m1, m2);
    let stats = engine.statistics();
    assert_eq!(stats.misses, 1);
    assert_eq!(stats.hits, 1);
    assert_eq!(stats.entries, 1);

    let mut studio = Studio::from_sample("demo", "arch");
    let scene = studio.compile_render_scene("Model");
    let frame = RenderFrame {
        viewport: Viewport::default(),
        background_argb: 0xFF10_1010,
        device_pixel_ratio: 1.0,
        ..Default::default()
    };
    let backend = SkiaRenderBackend::new();
    let svg = backend.render_svg(&scene, &frame);
    assert!(svg.starts_with("<svg"));
    assert!(svg.contains("</svg>"));
}

#[test]
fn shadow_studio_editor_symbols_agents_and_relay_parity() {
    let mut studio = Studio::from_sample("s1", "arch");
    let sub = studio.subscribe();
    assert_eq!(studio.active_subscriptions(), 1);
    let added = studio
        .add(&AddRequest {
            entity_type: "netDxf.Entities.Line".into(),
            properties: serde_json::json!({ "Layer": "0" }),
        })
        .expect("added");
    let assigned = studio.cad_assign_layer(&CadAssignLayerRequest {
        entities: vec![added.clone()],
        layer: "WALLS".into(),
    });
    assert_eq!(assigned, 1);
    assert!(studio.unsubscribe(&sub));
    let bytes = studio.export_dxf_bytes(false).expect("dxf export");
    assert!(!bytes.is_empty());

    let base_snap = studio.current_snapshot();
    let mut editor = EditorSession::new(Box::new(ShadowMemoryStore::new((*base_snap).clone())));
    let mut props = BTreeMap::new();
    props.insert("Layer".into(), ShadowValue::from_string("0"));
    let (eid, snap) = editor.create_entity("netDxf.Entities.Line", props).unwrap();
    assert!(snap.objects.contains_key(&eid));
    let preview = editor
        .preview_merge(&base_snap, &ShadowPreviewOptions::default())
        .unwrap();
    assert!(preview.removed >= 1);
    let _ = editor.delete_entities(&[eid]).unwrap();

    let conn = DomainConnectivity::build(
        "d1",
        vec![
            DomainPortNode { id: 0, position: Vec3::xy(0.0, 0.0), ..Default::default() },
            DomainPortNode { id: 1, position: Vec3::xy(0.01, 0.0), ..Default::default() },
            DomainPortNode { id: 2, position: Vec3::xy(0.02, 0.0), ..Default::default() },
        ],
        &DomainConnectivityOptions {
            tolerance: 0.015,
            ..Default::default()
        },
    );
    assert_eq!(conn.components.len(), 1);
    let iso = conn.analyze_isolation(&[1]);
    assert_eq!(iso.len(), 2);

    let sched = DomainSchedule::build(
        "d1",
        "Model",
        vec![DomainScheduleRow {
            key: "P-101".into(),
            class: "Pump".into(),
            ..Default::default()
        }],
        &DomainScheduleOptions::default(),
    );
    assert_eq!(sched.groups.len(), 1);

    let mut ws = MemoryAgentWorkspace::default();
    ws.write("/src/main.rs", "fn main() {}");
    assert_eq!(ws.read("src/main.rs").as_deref(), Some("fn main() {}"));
    assert_eq!(ws.search("main").len(), 1);
    let exported = ws.export();
    assert_eq!(exported.len(), 1);
    assert!(ws.delete("src/main.rs"));
    ws.import(vec![AgentFile { path: "a.txt".into(), content: "ok".into() }]);
    assert_eq!(ws.list().len(), 1);

    let mut ledger = BudgetLedger::default();
    let limits = EvaluationBudgetOptions::default();
    BudgetChatClient::charge(
        &mut ledger,
        &BudgetRequest { tokens: 100, charged_usd: 0.01, ..Default::default() },
        &limits,
    )
    .unwrap();
    assert_eq!(ledger.requests, 1);

    let mut relay = BrowserStudioRelay::new(4);
    relay.connect();
    assert!(relay.is_connected());
    relay.enqueue("req-1", serde_json::json!({"ok": true})).unwrap();
    assert_eq!(relay.pending_count(), 1);
    assert!(relay.cancel("req-1"));
    relay.stop();

    let mut ctrl = StudioAgentController::new(StudioAgentConfiguration::default());
    assert!(!ctrl.capabilities().is_empty());
    ctrl.companion_connect();
    assert!(ctrl.companion_connected);
    ctrl.cancel();
    assert!(ctrl.cancelled);
}
