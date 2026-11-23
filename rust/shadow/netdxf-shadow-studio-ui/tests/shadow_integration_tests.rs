use netdxf::entities::{Circle, EntityObject, Line};
use netdxf::{DxfDocument, Vector3};
use netdxf_shadow::{
    Affine3, IShadowStore, ObjectId, ShadowEditBatch, ShadowMerge, ShadowMergeChoice,
    ShadowMergeOptions, ShadowPropertyEdit, ShadowValue, Vec3,
};
use netdxf_shadow_geometry::{CadSceneBuilder, SceneOptions, SpatialIndex};
use netdxf_shadow_graph::{
    IndexedGraphAnalysis, IndexedGraphArc, IndexedGraphConnection, TopologyBuilder, TopologyOptions,
};
use netdxf_shadow_health::{DrawingHealth, DrawingHealthOptions, DrawingHealthResources};
use netdxf_shadow_netdxf::{ShadowCaptureOptions, ShadowSession};
use netdxf_shadow_rendering::{
    IRenderBackend, RenderCompileOptions, RenderCompiler, RenderFrame, SvgRenderBackend, Viewport,
};
use netdxf_shadow_studio_ui::{FerroStudioApp, StudioWebAssets};
use std::sync::Arc;

#[test]
fn shadow_snapshot_edit_and_three_way_merge() {
    let mut doc = DxfDocument::new();
    doc.entities.add(EntityObject::Line(Box::new(Line::new(
        Vector3::new(0.0, 0.0, 0.0),
        Vector3::new(10.0, 0.0, 0.0),
    ))));
    doc.entities.add(EntityObject::Circle(Box::new(Circle::new(
        Vector3::new(5.0, 5.0, 0.0),
        2.0,
    ))));

    let session = ShadowSession::from_document(&doc, "doc-merge", ShadowCaptureOptions::default());
    let ancestor = session.current();
    assert_eq!(ancestor.entity_ids.len(), 2);

    let ids: Vec<ObjectId> = ancestor.entity_ids.iter().cloned().collect();
    let batch_target = ShadowEditBatch::create(
        &ancestor,
        vec![ShadowPropertyEdit {
            object: ids[0].clone(),
            property: "Layer".into(),
            before: ShadowValue::from_string("0"),
            after: ShadowValue::from_string("WALLS"),
        }],
    );
    let target = Arc::new(ancestor.apply(&batch_target).unwrap());

    let batch_incoming = ShadowEditBatch::create(
        &ancestor,
        vec![ShadowPropertyEdit {
            object: ids[1].clone(),
            property: "Layer".into(),
            before: ShadowValue::from_string("0"),
            after: ShadowValue::from_string("PIPING"),
        }],
    );
    let incoming = Arc::new(ancestor.apply(&batch_incoming).unwrap());

    let plan = ShadowMerge::plan(
        ancestor,
        target,
        incoming,
        ShadowMergeOptions {
            conflict_choice: ShadowMergeChoice::Unresolved,
            ..Default::default()
        },
    )
    .unwrap();
    assert!(plan.can_apply());
    let merged = plan.preview.unwrap();
    assert_eq!(merged.read(ids[0].clone()).field("Layer").text(), "WALLS");
    assert_eq!(merged.read(ids[1].clone()).field("Layer").text(), "PIPING");
}

#[test]
fn shadow_pipeline_geometry_graph_health_svg_and_ferroui_skia_render() {
    let m = Affine3::move_by(Vec3::new(3.0, 4.0, 0.0)) * Affine3::rotate_z(std::f64::consts::FRAC_PI_2);
    let inv = m.try_inverse(1e-12).unwrap();
    let roundtrip = (inv * m).point(Vec3::new(7.0, -2.0, 5.0));
    assert!(Vec3::distance(roundtrip, Vec3::new(7.0, -2.0, 5.0)) < 1e-11);

    let mut app = FerroStudioApp::new("process-diagram");
    assert_eq!(StudioWebAssets::UI_FRAMEWORK, "FerroUI");
    assert_eq!(StudioWebAssets::RENDER_BACKEND, "Skia");

    let snap = app.studio.current_snapshot();
    let mut builder = CadSceneBuilder::new();
    let cad_scene = builder.build(&snap, &SceneOptions::default());
    assert_eq!(cad_scene.occurrences.len(), 2);

    let index = SpatialIndex::build(&cad_scene.occurrences);
    assert!(!index.query(cad_scene.bounds).is_empty());

    let topo = TopologyBuilder::build(&cad_scene, &TopologyOptions::default());
    assert!(!topo.edges.is_empty());
    let forest = topo.spanning_forest();
    assert!(forest.component_count >= 1);

    let scc = IndexedGraphAnalysis::strong_components(
        3,
        &[
            IndexedGraphArc { source: 0, target: 1 },
            IndexedGraphArc { source: 1, target: 2 },
            IndexedGraphArc { source: 2, target: 0 },
        ],
    );
    assert_eq!(scc.component_count, 1);

    let ll = IndexedGraphAnalysis::low_links(
        3,
        &[
            IndexedGraphConnection { a: 0, b: 1 },
            IndexedGraphConnection { a: 1, b: 2 },
        ],
    );
    assert_eq!(ll.bridges, vec![0, 1]);
    assert_eq!(ll.articulation_points, vec![1]);

    let health = DrawingHealth::analyze(
        &snap,
        &cad_scene,
        &DrawingHealthOptions::default(),
        &DrawingHealthResources::default(),
    );
    assert!(health.complete);

    let mut compiler = RenderCompiler::new();
    let render_scene = compiler.compile(&cad_scene, &RenderCompileOptions::default());
    let frame = RenderFrame {
        viewport: Viewport {
            width: 320.0,
            height: 200.0,
            center: Vec3::xy(50.0, 0.0),
            zoom: 2.0,
            rotation: 0.0,
        },
        background_argb: 0xFF18_1A1F,
        device_pixel_ratio: 1.0,
        ..Default::default()
    };
    let svg = SvgRenderBackend.render(&render_scene, &frame).unwrap();
    assert!(String::from_utf8(svg.bytes).unwrap().contains("<svg"));

    app.viewport = frame.viewport;
    let skia_img = app.render_viewport("Model").unwrap();
    assert_eq!(skia_img.width, 320);
    assert_eq!(skia_img.height, 200);
    assert_eq!(skia_img.bytes.len(), 320 * 200 * 4);
}
