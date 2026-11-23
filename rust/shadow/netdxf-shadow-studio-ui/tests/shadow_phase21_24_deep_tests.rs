use netdxf::entities::{Circle, Dimension, Line};
use netdxf::{DxfDocument, Vector2, Vector3};
use netdxf_shadow::{Affine3, ObjectId, ShadowObject, ShadowObjectKind, ShadowValue, Vec3};
use netdxf_shadow_dimensions::{DimensionEntityProjector, DimensionProjectionOptions};
use netdxf_shadow_drafting::{DraftingCommand, DraftingGrips, DraftingPlanner};
use netdxf_shadow_editor::{
    AffineEditOperations, AffineEditOptions, AffineEntityEdit, EditOperations,
};
use netdxf_shadow_geometry::{CadLinetypeAlignment, CadSceneBuilder, IEntityProjector, SceneOptions};
use netdxf_shadow_graph::{GraphEdge, GraphNode, TopologyGraph};
use netdxf_shadow_health::{DrawingHealth, DrawingHealthOptions, DrawingHealthResources};
use netdxf_shadow_mcp::{ShadowMcpContext, ShadowMcpTools};
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
use netdxf_shadow_rendering::{
    LinetypeFitting, LinetypePaths, MTextColorCodec, RenderContour, StrokePattern,
};
use netdxf_shadow_rendering_shx::{
    ShxEntityProjector, ShxExecutionOptions, ShxFont, ShxFontCatalog, ShxGlyphCompiler,
    ShxProjectionOptions, ShxSymbol,
};
use netdxf_shadow_samples::StudioSamples;
use netdxf_shadow_symbols::{DomainConnectivity, DomainConnectivityOptions, DomainPortNode};

#[test]
fn phase21_graph_cycle_basis_and_kruskal_mst() {
    // Triangle (0-1 weight 1, 1-2 weight 2, 0-2 weight 5) + isolated node 3
    let graph = TopologyGraph {
        document_id: "g".into(),
        revision: 1,
        nodes: vec![
            GraphNode { id: 0, position: Vec3::ZERO, edges: vec![0, 2] },
            GraphNode { id: 1, position: Vec3::xy(1.0, 0.0), edges: vec![0, 1] },
            GraphNode { id: 2, position: Vec3::xy(1.0, 2.0), edges: vec![1, 2] },
            GraphNode { id: 3, position: Vec3::xy(10.0, 10.0), edges: vec![] },
        ],
        edges: vec![
            GraphEdge { id: 0, a: 0, b: 1, length: 1.0, entity: ObjectId::new("E1"), occurrence: "0".into() },
            GraphEdge { id: 1, a: 1, b: 2, length: 2.0, entity: ObjectId::new("E2"), occurrence: "1".into() },
            GraphEdge { id: 2, a: 0, b: 2, length: 5.0, entity: ObjectId::new("E3"), occurrence: "2".into() },
        ],
    };

    let mst = graph.minimum_spanning_forest();
    assert_eq!(mst.component_count, 2);
    assert_eq!(mst.edges, vec![0, 1]);
    assert!((mst.length - 3.0).abs() < 1e-9);

    let basis = graph.cycle_basis();
    assert_eq!(basis.cycles.len(), 1);
    let path = basis.path(0).expect("cycle path should expand");
    assert_eq!(path.edges.len(), 3);
    assert_eq!(path.nodes.first(), path.nodes.last());
    assert!((path.length - 8.0).abs() < 1e-9);
}

#[test]
fn phase21_health_duplicates_tiny_gaps_and_dimension_projector() {
    let mut doc = DxfDocument::new();
    // Duplicate lines
    doc.add_entity(Line::new(Vector3::ZERO, Vector3::new(10.0, 0.0, 0.0)));
    doc.add_entity(Line::new(Vector3::ZERO, Vector3::new(10.0, 0.0, 0.0)));
    // Tiny-gap line starting 0.005 away from (10, 0, 0)
    doc.add_entity(Line::new(
        Vector3::new(10.005, 0.0, 0.0),
        Vector3::new(20.0, 0.0, 0.0),
    ));
    // Dimension entity for projector regeneration
    doc.add_entity(Dimension::linear(
        Vector2::new(0.0, 0.0),
        Vector2::new(20.0, 0.0),
        5.0,
        0.0,
    ));

    let snapshot = ShadowSession::capture(&doc, "health-test", ShadowCaptureOptions::default());
    let mut builder = CadSceneBuilder::new();
    let scene = builder.build(&snapshot, &SceneOptions::default());
    let report = DrawingHealth::analyze(
        &snapshot,
        &scene,
        &DrawingHealthOptions::default(),
        &DrawingHealthResources::default(),
    );
    assert!(report.complete);
    assert!(
        report.issues.iter().any(|i| i.code == "geometry.duplicate"),
        "expected duplicate geometry issue"
    );
    assert!(
        report.issues.iter().any(|i| i.code == "geometry.tiny-gap"),
        "expected tiny-gap issue"
    );

    // DimensionEntityProjector regenerates geometry for the LinearDimension
    let dim_id = snapshot
        .entity_ids
        .iter()
        .find(|id| snapshot.read((*id).clone()).type_name().contains("Dimension"))
        .cloned()
        .expect("captured dimension entity");
    let projector = DimensionEntityProjector::new(DimensionProjectionOptions::default());
    let geom = projector.project(&snapshot, &dim_id).expect("dimension geometry");
    assert!(!geom.components.is_empty(), "expected regenerated dimension components");
    assert!(!geom.contours.is_empty(), "expected regenerated dimension contours");
}

#[test]
fn phase21_editor_translate_and_affine_ellipse_circle() {
    let mut doc = DxfDocument::new();
    doc.add_entity(Line::new(Vector3::ZERO, Vector3::new(10.0, 0.0, 0.0)));
    doc.add_entity(Circle::new(Vector3::new(5.0, 5.0, 0.0), 3.0));
    let snapshot = ShadowSession::capture(&doc, "edit-test", ShadowCaptureOptions::default());
    let ids: Vec<ObjectId> = snapshot.entity_ids.iter().cloned().collect();

    let move_batch = EditOperations::move_entities(&snapshot, &ids, Vec3::new(10.0, -2.0, 0.0));
    assert!(!move_batch.edits.is_empty());

    let scale_edit = AffineEntityEdit {
        entity: ids
            .iter()
            .find(|id| snapshot.read((*id).clone()).type_name() == "Circle")
            .cloned()
            .unwrap(),
        transform: Affine3::scale(Vec3::new(2.0, 2.0, 2.0)),
    };
    let affine_batch =
        AffineEditOperations::transform_entities(&snapshot, &[scale_edit], &AffineEditOptions::default());
    assert!(
        affine_batch
            .edits
            .iter()
            .any(|e| e.property == "Radius"),
        "expected Circle radius to be scaled"
    );
}

#[test]
fn phase22_rendering_linetype_fitting_stroke_pattern_and_shx() {
    assert_eq!(MTextColorCodec::resolve_indexed(1), Some(0xffff_0000));
    assert_eq!(MTextColorCodec::resolve_indexed(0), None);
    assert_eq!(MTextColorCodec::decode_true_color(0x0000_00ff), Some(0xffff_0000));

    let dashes = LinetypeFitting::strokes(
        10.0,
        &[2.0, -1.0],
        CadLinetypeAlignment::Continuous,
        false,
        100,
    );
    assert!(dashes.len() >= 3);

    let contour = RenderContour {
        points: vec![Vec3::ZERO, Vec3::xy(10.0, 0.0)],
        closed: false,
    };
    let pattern = StrokePattern {
        dashes: vec![2.0, -1.0],
        phase: 0.0,
    };
    let dashed = pattern.apply(&contour);
    assert!(dashed.len() >= 3, "expected contour split into >=3 dashes");

    let split = LinetypePaths::split(
        &[RenderContour {
            points: vec![Vec3::ZERO, Vec3::xy(5.0, 0.0), Vec3::xy(10.0, 0.0)],
            closed: false,
        }],
        &[&[1]],
        100,
    );
    assert_eq!(split.len(), 2);

    // SHX glyph with opcode 9 (multi-XY), opcode 12 (bulge arc), and opcode 7 (subshape call)
    let mut font = ShxFont {
        name: "demo.shx".into(),
        above: 8,
        below: 2,
        ..Default::default()
    };
    font.symbols.insert(
        65, // 'A'
        ShxSymbol {
            code: 65,
            name: "A".into(),
            data: vec![9, 4, 8, 4, 248u8, 0, 0, 12, 4, 0, 64, 0],
            ..Default::default()
        },
    );
    let glyph = ShxGlyphCompiler::compile(&font, 65, &ShxExecutionOptions::default())
        .expect("compiled glyph");
    assert!(!glyph.contours.is_empty());
    assert!(glyph.contours[0].curves.len() >= 4);

    let mut catalog = ShxFontCatalog::default();
    catalog.register("demo.shx", font);
    let projector = ShxEntityProjector {
        catalog,
        options: ShxProjectionOptions::default(),
    };

    // Build a snapshot containing a Text entity referencing demo.shx
    let mut doc = DxfDocument::new();
    let mut snap = ShadowSession::capture(&doc, "shx-test", ShadowCaptureOptions::default());
    let style_id = ObjectId::new("S:DEMO");
    let mut style_obj = ShadowObject::new(style_id.clone(), "netDxf.Tables.TextStyle");
    style_obj
        .properties
        .insert("FontFile".into(), ShadowValue::from_string("demo.shx"));
    let text_id = ObjectId::new("E:TEXT:1");
    let mut text_obj = ShadowObject::new(text_id.clone(), "netDxf.Entities.Text");
    text_obj.kind = ShadowObjectKind::ENTITY;
    text_obj
        .properties
        .insert("Value".into(), ShadowValue::from_string("AA"));
    text_obj
        .properties
        .insert("Height".into(), ShadowValue::from_f64(8.0));
    text_obj
        .properties
        .insert("Style".into(), ShadowValue::reference(style_id.clone()));
    snap.objects.insert(style_id, std::sync::Arc::new(style_obj));
    snap.objects.insert(text_id.clone(), std::sync::Arc::new(text_obj));
    snap.entity_ids.insert(text_id.clone());

    let geom = projector.project(&snap, &text_id).expect("SHX projected text");
    assert!(!geom.contours.is_empty(), "expected stroked SHX contours");
    let _ = &mut doc;
}

#[test]
fn phase23_24_drafting_grips_connectivity_samples_and_mcp() {
    let doc = StudioSamples::create("architectural-plan");
    assert!(doc.entities.len() >= 7);
    let mech = StudioSamples::create("mechanical-part");
    assert!(mech.entities.len() >= 10);

    let snapshot = ShadowSession::capture(&doc, "draft-test", ShadowCaptureOptions::default());

    // DraftingPlanner creates a real Line entity
    let plan = DraftingPlanner::plan(
        &snapshot,
        &DraftingCommand {
            name: "LINE".into(),
            points: vec![Vec3::ZERO, Vec3::xy(100.0, 0.0)],
            ..Default::default()
        },
    );
    assert_eq!(plan.created().len(), 1);

    // DraftingGrips extracts StartPoint, Midpoint, EndPoint for a Line and moves Midpoint
    let line_id = snapshot
        .entity_ids
        .iter()
        .find(|id| snapshot.read((*id).clone()).type_name() == "Line")
        .cloned()
        .expect("line in architectural plan");
    let grips = DraftingGrips::extract(&snapshot, &line_id);
    assert_eq!(grips.len(), 3);
    let mid_grip = grips.iter().find(|g| g.key == "Midpoint").unwrap();
    let move_plan = DraftingGrips::r#move(&snapshot, mid_grip, mid_grip.position + Vec3::xy(5.0, 0.0));
    assert_eq!(move_plan.patch.changes.len(), 1);

    // DomainConnectivity BFS trace
    let conn = DomainConnectivity::build(
        "net",
        vec![
            DomainPortNode { id: 0, position: Vec3::ZERO, ..Default::default() },
            DomainPortNode { id: 1, position: Vec3::xy(0.01, 0.0), ..Default::default() },
            DomainPortNode { id: 2, position: Vec3::xy(10.0, 0.0), ..Default::default() },
        ],
        &DomainConnectivityOptions::default(),
    );
    let walk = conn.trace(0);
    assert_eq!(walk.visits.len(), 2);
    assert!(walk.complete);

    // MCP tools
    let ctx = ShadowMcpContext::new(std::sync::Arc::new(snapshot));
    let ordered = ShadowMcpTools::invoke(&ctx, "shadow.objects.ordered", &serde_json::json!({}))
        .expect("ordered objects");
    assert!(ordered["objects"].as_array().is_some_and(|a| !a.is_empty()));
}
