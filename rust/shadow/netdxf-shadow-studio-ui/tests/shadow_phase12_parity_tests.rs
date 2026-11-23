use netdxf::entities::{
    DxfOpaqueEntity, DxfTag, Image, Light, LightType, MTextBackgroundFill, Mesh, MeshEdge, Spline,
};
use netdxf::objects::{
    DxfSectionSettings, DxfSectionTypeSettings, DxfTableStyle, DxfTableStyleRow, DxfVbaProject,
};
use netdxf::{AciColor, Matrix3, Vector3};
use netdxf_shadow::{Bounds2, ObjectId, Vec3};
use netdxf_shadow_agents::{
    AgentOptions, AgentPrompt, AgentSession, ShadowAgentProviderOptions, ShadowAgentProviders,
};
use netdxf_shadow_agents_acp::{AcpClient, AcpClientServices, AcpConnectionOptions};
use netdxf_shadow_agents_evaluation::{EvaluationCase, EvaluationFixture};
use netdxf_shadow_agents_hosting::{
    ExternalAgentCall, ExternalAgentHost, ExternalAgentHostSettings,
};
use netdxf_shadow_disciplines::DomainValidation;
use netdxf_shadow_drafting::{DraftingHatches, DraftingPlanner, DraftingResources};
use netdxf_shadow_geometry::NurbsCurve;
use netdxf_shadow_mcp::ShadowJson;
use netdxf_shadow_rendering::{RichTextParagraphStyle, SvgRenderBackend, Viewport};
use netdxf_shadow_rendering_shx::{ShxBigFontMapFile, ShxBigFontMapReadOptions};
use netdxf_shadow_studio_core::{CadNewDocumentRequest, Studio, StudioDocumentHub};
use netdxf_shadow_symbols::{DomainMetadata, DomainRecord, DrawingBlockLibrary};
use std::collections::BTreeMap;

#[test]
fn core_phase5_to_10_features_behave_accurately() {
    // AciColor HSL & RGB roundtrip
    let red = AciColor::from_hsl(0.0, 1.0, 0.5);
    assert_eq!(red.to_color(), (255, 0, 0));
    let (h, s, l) = red.to_hsl();
    assert!(h.abs() < 1e-6 && (s - 1.0).abs() < 1e-6 && (l - 0.5).abs() < 1e-6);
    assert_eq!(AciColor::rgb_to_aci(255, 0, 0), 1);
    assert_eq!(AciColor::index_rgb(1), Some([255, 0, 0]));

    // MTextBackgroundFill
    let bg = MTextBackgroundFill::from_color(AciColor::YELLOW, 2.0);
    assert_eq!(bg.color_index(), Some(2));
    assert_eq!(bg.scale_factor, 2.0);

    // Mesh mutators
    let mut mesh = Mesh::new(
        vec![Vector3::ZERO, Vector3::UNIT_X, Vector3::UNIT_Y],
        vec![vec![0, 1, 2]],
    );
    mesh.edges.push(MeshEdge {
        start_vertex_index: 0,
        end_vertex_index: 1,
        crease: 0.0,
    });
    assert!(mesh.set_edge_crease(0, 1.5));
    assert!(mesh.set_vertex(2, Vector3::new(0.0, 2.0, 0.0)));
    assert_eq!(mesh.vertexes[2].y, 2.0);

    // Image & Light & OpaqueEntity transforms
    let mut img = Image::new("logo", Vector3::ZERO, 10.0, 5.0);
    img.transform_by(Matrix3::IDENTITY, Vector3::new(3.0, 4.0, 0.0));
    assert!((img.position.x - 3.0).abs() < 1e-9);
    let mut light = Light::new("L1", LightType::Spot, Vector3::ZERO);
    assert!(light.is_on());
    light.transform_by(Matrix3::IDENTITY, Vector3::new(1.0, 2.0, 3.0));
    assert_eq!(light.position, Vector3::new(1.0, 2.0, 3.0));

    let opaque = DxfOpaqueEntity::new("CUSTOM_ENT", vec![DxfTag::new(330, "1F")]);
    assert_eq!(opaque.references(), vec!["1F"]);

    // VBA project chunks
    let mut vba = DxfVbaProject::new();
    vba.set_chunks(&[vec![1, 2, 3], vec![4, 5]]).expect("chunks");
    assert_eq!(vba.data_length(), 5);
    assert_eq!(vba.chunks().len(), 1);

    // Section settings & TableStyle
    let mut sec = DxfSectionSettings::default();
    sec.set_type_settings(vec![DxfSectionTypeSettings::default()]);
    assert_eq!(sec.type_settings().len(), 1);
    let mut ts = DxfTableStyle::default_style();
    ts.rows.push(DxfTableStyleRow::default());
    assert!(ts.replace_style(0, DxfTableStyleRow::default().with_text_style("Standard")));

    // Spline point_at & set_uniform_weights
    let mut sp = Spline::from_control_points(
        vec![
            Vector3::new(0.0, 0.0, 0.0),
            Vector3::new(1.0, 2.0, 0.0),
            Vector3::new(2.0, 2.0, 0.0),
            Vector3::new(3.0, 0.0, 0.0),
        ],
        3,
        false,
    );
    sp.set_uniform_weights();
    let mid = sp.point_at(0.5).expect("midpoint");
    assert!((mid.x - 1.5).abs() < 1e-6);
}

#[test]
fn shadow_nurbs_cox_de_boor_svg_viewport_and_shx_bigfont() {
    // True quadratic rational Bezier (quarter circle of radius 1) via Cox-de Boor NURBS!
    let w = std::f64::consts::FRAC_1_SQRT_2;
    let nurbs = NurbsCurve {
        degree: 2,
        control_points: vec![
            Vec3::xy(1.0, 0.0),
            Vec3::xy(1.0, 1.0),
            Vec3::xy(0.0, 1.0),
        ],
        weights: vec![1.0, w, 1.0],
        knots: vec![0.0, 0.0, 0.0, 1.0, 1.0, 1.0],
    };
    let mid = nurbs.at(0.5);
    assert!((mid.length() - 1.0).abs() < 1e-9);
    assert!((mid.x - w).abs() < 1e-9);
    assert!((mid.y - w).abs() < 1e-9);

    // Viewport fit & bounds
    let mut vp = Viewport::default();
    vp.fit(Bounds2::new(0.0, 0.0, 100.0, 50.0), 0.1);
    assert!(vp.validate().is_ok());
    assert!((vp.center.x - 50.0).abs() < 1e-9);

    // SVG helpers & paragraph style validation
    assert_eq!(SvgRenderBackend::color(0xFF11_2233), "#112233");
    assert!(SvgRenderBackend::path("M0,0 L10,10", 0xFFFF_0000, 1.0, None).contains("#ff0000"));
    assert!(RichTextParagraphStyle::default().validate().is_ok());

    // SHX BigFont map roundtrip
    let map = ShxBigFontMapFile::read_strict("0x8140=U+3000\n", &ShxBigFontMapReadOptions::default())
        .expect("parse map");
    assert_eq!(ShxBigFontMapFile::map(&map, '\u{3000}'), Some(0x8140));
    let written = ShxBigFontMapFile::write(&map);
    let reparsed = ShxBigFontMapFile::read(&written, &ShxBigFontMapReadOptions::default());
    assert!(ShxBigFontMapFile::equivalent(&map, &reparsed));
}

#[test]
fn shadow_studio_hub_agents_acp_hosting_mcp_and_symbols() {
    let mut hub = StudioDocumentHub::new();
    hub.create(&CadNewDocumentRequest {
        name: "plan-1".into(),
        sample: Some("line".into()),
    });
    assert!(hub.owned("plan-1"));
    assert_eq!(hub.documents(), vec!["plan-1"]);
    let dxf = hub.save("plan-1", false).expect("save dxf");
    assert!(!dxf.is_empty());

    let mut studio = Studio::from_sample("doc-cmd", "line");
    assert!(studio.cad_create_block("VALVE", Vec3::ZERO, Vec::new()));
    let hatch_id = studio
        .cad_create_hatch("0", "ANSI31", 1.0, 0.0)
        .expect("hatch");
    assert!(studio.cad_change_hatch_pattern(&hatch_id, "SOLID", 1.0, 0.0));
    let entries = DrawingBlockLibrary::entries(&studio.current_snapshot());
    assert!(!entries.is_empty());

    assert!(DraftingHatches::pattern_names().contains(&"ANSI31"));
    assert!(DraftingResources::reserved("layer").contains(&"0"));
    let _ = DraftingPlanner::empty_plan(&studio.current_snapshot(), "NOOP");

    // DomainMetadata & DomainValidation
    let rec = DomainRecord {
        id: ObjectId::new("O:1"),
        domain: "pid".into(),
        symbol_id: "valve".into(),
        tag: "V-101".into(),
        properties: BTreeMap::new(),
        passages: Vec::new(),
        references: Vec::new(),
        ..Default::default()
    };
    assert!(DomainMetadata::validate(&rec).is_empty());
    let encoded = DomainMetadata::write(&rec).expect("encode");
    assert_eq!(DomainMetadata::read(&encoded).expect("decode").tag, "V-101");
    let mut val = DomainValidation::new();
    assert!(val.positive(4.2, "diameter"));
    assert!(val.consistent_routes(&[("A", "B")]));
    assert!(val.value());

    // AgentSession, Providers, ACP, Hosting, Evaluation, ShadowJson
    let mut session = AgentSession::new(AgentOptions::default());
    session.run(AgentPrompt {
        text: "Inspect drawing".into(),
        ..Default::default()
    });
    session.execute_tool("workspace.write", &serde_json::json!({ "path": "a.txt", "content": "hi" }));
    assert_eq!(session.workspace.read("a.txt").as_deref(), Some("hi"));
    let prov = ShadowAgentProviders::create(ShadowAgentProviderOptions {
        provider: "openai".into(),
        model: "gpt-5".into(),
        base_url: None,
        ..Default::default()
    })
    .expect("provider");
    assert_eq!(ShadowAgentProviders::get_service(&prov, "model"), Some("gpt-5"));

    let mut acp = AcpClient::new(AcpConnectionOptions::default(), AcpClientServices::default());
    acp.start();
    assert!(acp.is_connected());
    let init_req = acp.initialize(serde_json::json!({ "protocolVersion": 1 }));
    assert_eq!(init_req["method"], "initialize");

    let mut host = ExternalAgentHost::new(ExternalAgentHostSettings::default());
    host.start();
    let reply = host.call(ExternalAgentCall {
        id: "c1".into(),
        method: "ping".into(),
        params: serde_json::Value::Null,
    });
    assert!(reply.ok);

    let case = EvaluationCase {
        id: "eval-1".into(),
        prompt: "draw line".into(),
        ..Default::default()
    };
    assert_eq!(EvaluationFixture::freeze(&case).len(), 16);

    let mut json_val = serde_json::json!({ "a": { "b": 1 } });
    assert!(ShadowJson::replace(&mut json_val, "/a/b", serde_json::json!(2)));
    assert_eq!(ShadowJson::locate(&json_val, "/a/b"), Some(&serde_json::json!(2)));
}
