use netdxf::entities::{
    Arc as DxfArc, Ellipse as DxfEllipse, EntityObject, Line as DxfLine, Polyline2D,
    Polyline2DVertex, Solid as DxfSolid, Text as DxfText,
};
use netdxf::{DxfDocument, Vector2, Vector3};
use netdxf_shadow::{
    IShadowStore, ObjectId, ShadowEditBatch, ShadowMemoryStore, ShadowPropertyEdit, ShadowValue,
    Vec3,
};
use netdxf_shadow_drafting::PlanarGeometry;
use netdxf_shadow_editor::{
    EditorSession, ShadowEditPreview, ShadowPreviewOptions, Snapping,
};
use netdxf_shadow_electrical::ElectricalSymbols;
use netdxf_shadow_geometry::{CadSceneBuilder, SceneOptions};
use netdxf_shadow_graph::{
    GraphVisualizationOptions, TopologyBuilder, TopologyOptions,
};
use netdxf_shadow_mcp::{McpJsonRpcSession, McpToolRegistry};
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
use netdxf_shadow_pid::PidSymbols;
use netdxf_shadow_rendering::{
    ApproximateTextLayoutEngine, LinetypePlanner, PerspectiveCamera, PerspectiveOptions, RichText,
    RichTextLayout, RichTextParseOptions, RichTextStyle, TextDecoration,
};
use netdxf_shadow_rendering_shx::{
    ShxEncoding, ShxExecutionOptions, ShxFontReader, ShxGlyphCompiler, ShxReadOptions, ShxSymbol,
};
use netdxf_shadow_studio_ui::FerroStudioApp;
use netdxf_shadow_symbols::{
    DomainConnectivity, DomainConnectivityOptions, DomainConnectivityWalk, DomainPortNode,
};

#[test]
fn capture_all_entities_project_and_write_back() {
    let mut doc = DxfDocument::new();
    doc.entities.add(EntityObject::Line(Box::new(DxfLine::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
    ))));
    doc.entities.add(EntityObject::Arc(Box::new(DxfArc::new(
        Vector3::new(0.0, 0.0, 0.0),
        5.0,
        0.0,
        90.0,
    ))));
    doc.entities.add(EntityObject::Ellipse(Box::new(DxfEllipse::new(
        Vector3::new(20.0, 0.0, 0.0),
        10.0,
        4.0,
    ))));
    doc.entities.add(EntityObject::Polyline2D(Box::new(Polyline2D::new(
        vec![
            Polyline2DVertex::with_bulge(0.0, 0.0, 0.5),
            Polyline2DVertex::new(10.0, 0.0),
        ],
        false,
    ))));
    doc.entities.add(EntityObject::Solid(Box::new(DxfSolid::new(
        Vector2::new(0.0, 0.0),
        Vector2::new(2.0, 0.0),
        Vector2::new(2.0, 2.0),
        Vector2::new(0.0, 2.0),
    ))));
    doc.entities.add(EntityObject::Text(Box::new(DxfText::new(
        "HELLO",
        Vector3::new(1.0, 1.0, 0.0),
        2.5,
    ))));

    let session = ShadowSession::from_document(&doc, "doc-all", ShadowCaptureOptions::default());
    let snap = session.current();
    assert_eq!(snap.entity_ids.len(), 6);

    let mut builder = CadSceneBuilder::new();
    let scene = builder.build(&snap, &SceneOptions::default());
    assert_eq!(scene.occurrences.len(), 6);
    assert!(scene.occurrences.iter().any(|o| o.geometry.filled));
    assert!(scene.occurrences.iter().any(|o| !o.geometry.texts.is_empty()));

    // Edit entity 0 layer and write back into live DxfDocument
    let first_id = ObjectId::new("O:Entity:0");
    let batch = ShadowEditBatch::create(
        &snap,
        vec![ShadowPropertyEdit {
            object: first_id,
            property: "Layer".into(),
            before: ShadowValue::from_string("0"),
            after: ShadowValue::from_string("MODIFIED"),
        }],
    );
    session.apply(&batch).unwrap();
    let changed = session.write_back(&mut doc);
    assert_eq!(changed, 1);
    assert_eq!(doc.entities.iter().next().unwrap().common().layer, "MODIFIED");
}

#[test]
fn graph_shortest_path_cycle_basis_and_visualization() {
    let mut doc = DxfDocument::new();
    // Square loop (4 nodes, 4 edges -> 1 fundamental cycle)
    let pts = [
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
        Vector3::new(10.0, 10.0, 0.0),
        Vector3::new(0.0, 10.0, 0.0),
    ];
    for i in 0..4 {
        doc.entities.add(EntityObject::Line(Box::new(DxfLine::new(
            pts[i],
            pts[(i + 1) % 4],
        ))));
    }
    let session = ShadowSession::from_document(&doc, "doc-loop", ShadowCaptureOptions::default());
    let mut builder = CadSceneBuilder::new();
    let scene = builder.build(&session.current(), &SceneOptions::default());
    let graph = TopologyBuilder::build(&scene, &TopologyOptions::default());
    assert_eq!(graph.nodes.len(), 4);
    assert_eq!(graph.edges.len(), 4);

    let basis = graph.cycle_basis();
    assert_eq!(basis.cycles.len(), 1);

    let sp = graph.shortest_path(0, 2).unwrap();
    assert!((sp.length - 20.0).abs() < 1e-6);

    let viz = graph.visualize(&GraphVisualizationOptions { offset: 0, limit: 2 });
    assert_eq!(viz.cycle_rank, 1);
    assert_eq!(viz.nodes.items.len(), 2);
    assert_eq!(viz.nodes.next_offset, Some(2));
}

#[test]
fn rich_text_camera_linetype_and_shx_bytecode_verification() {
    let parsed = RichText::parse(
        "Line1\\P{\\L\\fArial|b1|i1;BoldItalic}\\P{\\H2x;Tall}",
        RichTextStyle::default(),
        &RichTextParseOptions::default(),
    );
    assert!(parsed.inlines.iter().any(|i| i.style.bold && i.style.italic && i.style.decoration == TextDecoration::Underline));
    assert!(parsed.inlines.iter().any(|i| (i.style.height - 5.0).abs() < 1e-9));

    let layout = RichTextLayout::layout(&parsed, &ApproximateTextLayoutEngine, 1.2);
    assert_eq!(layout.lines.len(), 3);
    assert!(layout.total_width > 0.0 && layout.total_height > 0.0);

    let cam = PerspectiveCamera {
        eye: Vec3::new(0.0, 0.0, 10.0),
        target: Vec3::ZERO,
        up: Vec3::UNIT_Y,
        options: PerspectiveOptions::default(),
    };
    let proj = cam.project_point(Vec3::ZERO, 800.0, 600.0).unwrap();
    assert!((proj.x - 400.0).abs() < 1e-6 && (proj.y - 300.0).abs() < 1e-6);
    let ray = cam.unproject_ray(400.0, 300.0, 800.0, 600.0);
    assert!((ray.direction.z + 1.0).abs() < 1e-6);

    let placements = LinetypePlanner::plan(&[Vec3::ZERO, Vec3::xy(10.0, 0.0)], 2.5);
    assert_eq!(placements.len(), 4);

    let mut font = ShxFontReader::read(
        b"AutoCAD-86 unifont 1.0\r\n\x1A",
        "txt.shx",
        &ShxReadOptions::default(),
    )
    .unwrap();
    assert_eq!(font.encoding, ShxEncoding::Unicode);
    // Bytecode: pen down (1), move (8, dx=4, dy=0), pen up (2), move (8, dx=2, dy=0), end (0)
    font.symbols.insert(
        65,
        ShxSymbol {
            code: 65,
            name: "A".into(),
            data: vec![1, 8, 4, 0, 2, 8, 2, 0, 0],
        },
    );
    let glyph = ShxGlyphCompiler::compile(&font, 65, &ShxExecutionOptions::default()).unwrap();
    assert_eq!(glyph.contours.len(), 1);
    assert_eq!(glyph.contours[0].curves.len(), 1);
}

#[test]
fn editor_undo_redo_planar_geometry_symbols_mcp_and_ferroui() {
    let mut doc = DxfDocument::new();
    doc.entities.add(EntityObject::Line(Box::new(DxfLine::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
    ))));
    let snap = ShadowSession::capture(&doc, "doc-ed", ShadowCaptureOptions::default());
    let mut editor = EditorSession::new(Box::new(ShadowMemoryStore::new(snap)));
    let cur = editor.current();
    let batch = ShadowEditBatch::create(
        &cur,
        vec![ShadowPropertyEdit {
            object: ObjectId::new("O:Entity:0"),
            property: "Layer".into(),
            before: ShadowValue::from_string("0"),
            after: ShadowValue::from_string("EDITED"),
        }],
    );
    editor.apply_batch("Change Layer", &batch).unwrap();
    assert_eq!(
        editor.current().read(ObjectId::new("O:Entity:0")).field("Layer").text(),
        "EDITED"
    );
    assert!(editor.state().can_undo);
    editor.undo().unwrap();
    assert_eq!(
        editor.current().read(ObjectId::new("O:Entity:0")).field("Layer").text(),
        "0"
    );
    editor.redo().unwrap();
    assert_eq!(
        editor.current().read(ObjectId::new("O:Entity:0")).field("Layer").text(),
        "EDITED"
    );

    let snap_res = Snapping::snap_to_endpoints(
        Vec3::xy(9.9, 0.05),
        &[Vec3::ZERO, Vec3::xy(10.0, 0.0)],
        0.5,
    )
    .unwrap();
    assert_eq!(snap_res.point, Vec3::xy(10.0, 0.0));

    let patch = netdxf_shadow::ShadowGraphPatch::between(&cur, &editor.current()).unwrap();
    let summary = ShadowEditPreview::summarize(&patch, &ShadowPreviewOptions::default());
    assert_eq!(summary.modified, 1);
    assert_eq!(summary.changes.len(), 1);

    let ix = PlanarGeometry::segment_intersection_2d(
        Vec3::xy(0.0, 0.0),
        Vec3::xy(10.0, 10.0),
        Vec3::xy(0.0, 10.0),
        Vec3::xy(10.0, 0.0),
    )
    .unwrap();
    assert!(Vec3::distance(ix, Vec3::xy(5.0, 5.0)) < 1e-9);
    assert!((PlanarGeometry::distance_to_segment_2d(Vec3::xy(5.0, 3.0), Vec3::ZERO, Vec3::xy(10.0, 0.0)) - 3.0).abs() < 1e-9);
    let area = PlanarGeometry::signed_polygon_area(&[
        Vec3::xy(0.0, 0.0),
        Vec3::xy(4.0, 0.0),
        Vec3::xy(4.0, 3.0),
        Vec3::xy(0.0, 3.0),
    ]);
    assert!((area - 12.0).abs() < 1e-9);

    assert!(!ElectricalSymbols::catalog().is_empty());
    assert!(!PidSymbols::catalog().is_empty());

    let ports = vec![
        DomainPortNode {
            id: 0,
            entity: ObjectId::new("E1"),
            port_name: "OUT".into(),
            position: Vec3::xy(0.0, 0.0),
        },
        DomainPortNode {
            id: 1,
            entity: ObjectId::new("E2"),
            port_name: "IN".into(),
            position: Vec3::xy(0.01, 0.0),
        },
        DomainPortNode {
            id: 2,
            entity: ObjectId::new("E3"),
            port_name: "IN".into(),
            position: Vec3::xy(100.0, 0.0),
        },
    ];
    let (conns, comps) = DomainConnectivity::analyze(&ports, &DomainConnectivityOptions::default());
    assert_eq!(conns.len(), 1);
    assert_eq!(comps.len(), 2);
    let proof = DomainConnectivityWalk::prove_connection(3, &conns, 0, 1);
    assert!(proof.connected);
    assert!(!DomainConnectivityWalk::prove_connection(3, &conns, 0, 2).connected);

    let mcp = McpJsonRpcSession::new(McpToolRegistry::new());
    let init = mcp.handle_request(&serde_json::json!({
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize"
    }));
    assert_eq!(init["result"]["protocolVersion"], "2024-11-05");

    let mut app = FerroStudioApp::new("process-diagram");
    app.zoom_to_fit("Model", 0.1);
    let hits = app.pick("Model", app.viewport.width * 0.5, app.viewport.height * 0.5, 50.0);
    assert!(!hits.is_empty());
}
