use netdxf::entities::{
    Dimension, DimensionBlock, DimensionType, HatchGradientPattern, HatchGradientPatternType,
    Leader,
};
use netdxf::objects::{
    DxfFieldDataValue, DxfFieldResult, DxfFieldResultEdit, DxfFieldResultStatus, DxfStoredField,
};
use netdxf::tables::DimensionStyle;
use netdxf::{AciColor, Matrix3, Vector2, Vector3};
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowMemoryStore, ShadowObject, ShadowSnapshot, ShadowValue, Vec3};
use netdxf_shadow_agents::DirectoryAgentWorkspace;
use netdxf_shadow_drafting::{
    DraftingLayerStates, LayerStateEdit, LayerStateFields, LayerStateImportOptions, PlanarGeometry,
};
use netdxf_shadow_editor::EditorSession;
use netdxf_shadow_mcp::{ShadowMcpContext, ShadowMcpTools};
use netdxf_shadow_rendering_shx::{ShxFontReader, ShxReadOptions};
use netdxf_shadow_studio_ui::{FerroStudioApp, FerroStudioPanel};
use std::collections::{BTreeMap, BTreeSet};
use std::sync::Arc;

#[test]
fn planar_geometry_three_point_arc_and_intersections() {
    let arc = PlanarGeometry::three_point_arc(
        Vec3::xy(1.0, 0.0),
        Vec3::xy(0.0, 1.0),
        Vec3::xy(-1.0, 0.0),
        1e-9,
    )
    .expect("valid semicircle");
    assert!((arc.radius - 1.0).abs() < 1e-9);
    assert!(arc.center.length() < 1e-9);
    assert!((arc.start_degrees - 0.0).abs() < 1e-6);
    assert!((arc.end_degrees - 180.0).abs() < 1e-6);

    let (t, u) = PlanarGeometry::line_intersection(
        Vec3::xy(0.0, 0.0),
        Vec3::xy(10.0, 10.0),
        Vec3::xy(0.0, 10.0),
        Vec3::xy(10.0, 0.0),
    )
    .expect("crosses at midpoint");
    assert!((t - 0.5).abs() < 1e-9);
    assert!((u - 0.5).abs() < 1e-9);

    let hits = PlanarGeometry::line_circle(
        Vec3::xy(-2.0, 0.0),
        Vec3::xy(2.0, 0.0),
        Vec3::ZERO,
        1.0,
    );
    assert_eq!(hits.len(), 2);
    assert!((hits[0] - 0.25).abs() < 1e-9);
    assert!((hits[1] - 0.75).abs() < 1e-9);

    let box2 = Bounds2::new(0.0, 0.0, 5.0, 5.0);
    assert!(PlanarGeometry::segment_intersects_box(
        Vec3::xy(-1.0, 2.0),
        Vec3::xy(6.0, 2.0),
        box2
    ));
    assert!(!PlanarGeometry::segment_intersects_box(
        Vec3::xy(-1.0, 6.0),
        Vec3::xy(6.0, 6.0),
        box2
    ));
}

#[test]
fn editor_session_translate_transform_and_layer_states_roundtrip() {
    let root_id = ObjectId::new("O:Doc");
    let line_id = ObjectId::new("O:Line1");
    let mut line_obj = ShadowObject::new(line_id.clone(), "netDxf.Entities.Line");
    line_obj
        .properties
        .insert("StartPoint".into(), ShadowValue::from_vec3(Vec3::xy(0.0, 0.0), false));
    line_obj
        .properties
        .insert("EndPoint".into(), ShadowValue::from_vec3(Vec3::xy(10.0, 0.0), false));
    line_obj.writable_properties.insert("StartPoint".into());
    line_obj.writable_properties.insert("EndPoint".into());

    let mut objects = BTreeMap::new();
    objects.insert(root_id.clone(), Arc::new(ShadowObject::new(root_id.clone(), "netDxf.DxfDocument")));
    objects.insert(line_id.clone(), Arc::new(line_obj));
    let mut entity_ids = BTreeSet::new();
    entity_ids.insert(line_id.clone());

    let snap = ShadowSnapshot::new("doc-ed", 0, root_id, objects, entity_ids, Vec::new());
    let mut editor = EditorSession::new(Box::new(ShadowMemoryStore::new(snap)));
    editor.toggle_selection(line_id.clone());
    assert_eq!(editor.selection.len(), 1);

    editor.translate(Vec3::xy(5.0, 2.0)).expect("translate");
    let after_t = editor.snapshot();
    let end_pt = after_t.read(line_id.clone()).field("EndPoint").vector(Vec3::ZERO);
    assert!((end_pt.x - 15.0).abs() < 1e-9);
    assert!((end_pt.y - 2.0).abs() < 1e-9);

    let scale = Affine3::new(
        Vec3::new(2.0, 0.0, 0.0),
        Vec3::new(0.0, 2.0, 0.0),
        Vec3::UNIT_Z,
        Vec3::ZERO,
    );
    editor.transform(&scale).expect("transform");
    let after_s = editor.snapshot();
    let end_pt2 = after_s.read(line_id.clone()).field("EndPoint").vector(Vec3::ZERO);
    assert!((end_pt2.x - 30.0).abs() < 1e-9);
    assert_eq!(editor.undo_descriptions().len(), 2);

    // Layer state planning
    let plan = DraftingLayerStates::plan(
        &after_s,
        &LayerStateEdit {
            action: "save".into(),
            name: "PlanA".into(),
            fields: LayerStateFields::All,
            ..Default::default()
        },
    )
    .expect("plan save");
    let after_ls = editor.apply_patch("save ls", &plan.patch).expect("apply ls");
    let states = DraftingLayerStates::read(&after_ls);
    assert_eq!(states.len(), 1);
    let transfer = DraftingLayerStates::export(&after_ls, &states[0].id).expect("export");
    let analysis = DraftingLayerStates::analyze_import(
        &after_ls,
        &transfer,
        &LayerStateImportOptions { overwrite_existing: true },
    );
    assert!(analysis.conflicts.is_empty());
}

#[test]
fn shx_reader_mcp_tools_ferro_studio_and_directory_workspace() {
    let shp = "*0,4,TEST\n8,2,0,0\n*65,3,A\n0x01,0x20,0\n";
    let font = ShxFontReader::read_ascii(shp, "test.shp", &ShxReadOptions::default()).expect("parse shp");
    assert_eq!(font.above, 8);
    assert_eq!(font.below, 2);
    assert!(font.symbols.contains_key(&65));

    let mut app = FerroStudioApp::new("line");
    app.toggle_panel(FerroStudioPanel::Layers);
    let svg = app.render_svg("Model");
    assert!(svg.contains("<svg"));
    let dxf_bytes = app.export_dxf_bytes().expect("export dxf");
    assert!(!dxf_bytes.is_empty());

    let ctx = ShadowMcpContext::new(app.current_snapshot());
    let summary = ShadowMcpTools::invoke(&ctx, "shadow.document.summary", &serde_json::json!({}))
        .expect("mcp summary");
    assert!(summary.get("objectCount").is_some());

    let tmp_dir = std::env::temp_dir().join("netdxf_shadow_ws_phase4_test");
    let _ = std::fs::remove_dir_all(&tmp_dir);
    let ws = DirectoryAgentWorkspace::new(tmp_dir.to_string_lossy().to_string());
    ws.write("notes/plan.txt", "hello shadow").expect("write");
    assert_eq!(ws.read("notes/plan.txt").expect("read"), "hello shadow");
    assert!(ws.read("../escape.txt").is_err());
    let hits = ws.search("shadow").expect("search");
    assert_eq!(hits.len(), 1);
    let _ = std::fs::remove_dir_all(&tmp_dir);
}

#[test]
fn core_dimension_block_angular_ordinate_leader_gradient_and_fields() {
    let style = DimensionStyle::default_style();
    let mut ang = Dimension::linear(Vector2::ZERO, Vector2::new(10.0, 0.0), 5.0, 0.0);
    ang.dimension_type = DimensionType::Angular3Point;
    ang.definition_point = Vector3::ZERO;
    ang.first_reference_point = Vector3::new(10.0, 0.0, 0.0);
    ang.second_reference_point = Vector3::new(0.0, 10.0, 0.0);
    ang.arc_definition_point = Vector3::new(5.0, 5.0, 0.0);
    ang.measurement = 90.0;
    let ang_block = DimensionBlock::build_angular3_point(&ang, &style, "*D10");
    assert!(ang_block.entities.len() >= 5);

    let mut ord = Dimension::linear(Vector2::ZERO, Vector2::new(10.0, 0.0), 5.0, 0.0);
    ord.dimension_type = DimensionType::Ordinate;
    ord.first_reference_point = Vector3::new(10.0, 20.0, 0.0);
    ord.second_reference_point = Vector3::new(15.0, 30.0, 0.0);
    ord.measurement = 10.0;
    let ord_block = DimensionBlock::build_ordinate(&ord, &style, "*D11");
    assert!(ord_block.entities.len() >= 4);

    let mut leader = Leader::new(vec![Vector2::ZERO, Vector2::new(5.0, 5.0)]);
    leader.has_hookline = true;
    leader.update(2.0);
    assert_eq!(leader.vertexes.len(), 3);
    leader.transform_by(Matrix3::IDENTITY, Vector3::new(1.0, 2.0, 3.0));
    assert!((leader.vertexes[0].x - 1.0).abs() < 1e-9);
    assert!((leader.elevation - 3.0).abs() < 1e-9);

    let grad = HatchGradientPattern::two_color_gradient(
        AciColor::RED,
        AciColor::BLUE,
        HatchGradientPatternType::Linear,
    );
    assert_eq!(grad.color1_aci_index(), 1);
    assert_eq!(grad.color2_aci_index(), 5);

    let mut field = DxfStoredField::new("AcExpr", "%<\\AcExpr (2+2)>%");
    field.apply_result(&DxfFieldResultEdit {
        status: DxfFieldResultStatus::Success,
        formatted_text: "4".into(),
        value: DxfFieldDataValue::Integer(4),
    });
    assert_eq!(field.evaluated_text, "4");
    let fail = DxfFieldResult::failure_with_value(
        DxfFieldResultStatus::SyntaxError,
        12,
        "bad syntax",
        "cached",
    );
    assert!(fail.retains_cached_value());
    assert_eq!(fail.error_code(), 12);
    assert_eq!(fail.formatted_text(), "cached");
}
