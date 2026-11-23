use crate::curves::Curve3;
use netdxf_shadow::{Affine3, Bounds2, ObjectId, ShadowSnapshot, Vec3};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadFillRule {
    #[default]
    EvenOdd,
    NonZero,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadStyleSource {
    #[default]
    Explicit,
    ByLayer,
    ByBlock,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadLinetypeDecorationKind {
    #[default]
    Text,
    Shape,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadLinetypeRotation {
    #[default]
    Relative,
    Upright,
    Absolute,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum CadLinetypeAlignment {
    #[default]
    Continuous,
    PerSegment,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadLinetypeDecoration {
    pub kind: CadLinetypeDecorationKind,
    pub content: String,
    pub resource: String,
    pub rotation: CadLinetypeRotation,
    pub angle: f64,
    pub scale: f64,
    pub offset: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadLinetypeElement {
    pub length: f64,
    pub decoration: Option<CadLinetypeDecoration>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadLinetype {
    pub elements: Vec<CadLinetypeElement>,
    pub scale: f64,
    pub normal: Vec3,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadStyle {
    pub argb: u32,
    pub lineweight: f64,
    pub dashes: Vec<f64>,
    pub visible: bool,
    pub layer: String,
    pub linetype: Option<CadLinetype>,
    pub alignment: CadLinetypeAlignment,
}

impl Default for CadStyle {
    fn default() -> Self {
        Self {
            argb: 0xFFFF_FFFF,
            lineweight: 0.25,
            dashes: Vec::new(),
            visible: true,
            layer: "0".into(),
            linetype: None,
            alignment: CadLinetypeAlignment::Continuous,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadComponentStyle {
    pub value: CadStyle,
    pub color: CadStyleSource,
    pub lineweight: CadStyleSource,
    pub linetype: CadStyleSource,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadContour {
    pub curves: Vec<Curve3>,
    pub closed: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadStrokeGroup {
    pub contours: Vec<CadContour>,
    pub argb: Option<u32>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadGradient {
    pub kind: String,
    pub color1: u32,
    pub color2: u32,
    pub frame: Affine3,
    pub shift: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadRegion {
    pub contours: Vec<CadContour>,
    pub fill_rule: CadFillRule,
    pub argb: Option<u32>,
    pub gradient: Option<CadGradient>,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct CadLineSpacing {
    pub factor: f64,
    pub exact: bool,
}

impl Default for CadLineSpacing {
    fn default() -> Self {
        Self {
            factor: 1.0,
            exact: false,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadText {
    pub content: String,
    pub origin: Vec3,
    pub x_axis: Vec3,
    pub y_axis: Vec3,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
    pub font: String,
    pub is_mtext: bool,
    pub line_spacing: CadLineSpacing,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadImage {
    pub file_name: String,
    pub corners: Vec<Vec3>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct CadGeometry {
    pub contours: Vec<CadContour>,
    pub filled: bool,
    pub regions: Vec<CadRegion>,
    pub stroke_groups: Vec<CadStrokeGroup>,
    pub texts: Vec<CadText>,
    pub images: Vec<CadImage>,
    pub points: Vec<Vec3>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadComponent {
    pub key: String,
    pub geometry: CadGeometry,
    pub style: Option<CadComponentStyle>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadOccurrence {
    pub key: String,
    pub entity: ObjectId,
    pub insert_path: Vec<ObjectId>,
    pub transform: Affine3,
    pub style: CadStyle,
    pub geometry: CadGeometry,
    pub bounds: Bounds2,
    pub components: Vec<CadComponent>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadScene {
    pub document_id: String,
    pub revision: i64,
    pub layout: String,
    pub occurrences: Vec<CadOccurrence>,
    pub bounds: Bounds2,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct SceneOptions {
    pub layout: String,
    pub maximum_depth: usize,
    pub maximum_occurrences: usize,
    pub include_invisible: bool,
}

impl Default for SceneOptions {
    fn default() -> Self {
        Self {
            layout: "Model".into(),
            maximum_depth: 64,
            maximum_occurrences: 1_000_000,
            include_invisible: false,
        }
    }
}

/// Projects a single shadow entity into [`CadGeometry`].
pub trait IEntityProjector: Send + Sync {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry>;
}

/// Cacheable entity projector with deterministic dependency tracking.
pub trait ICacheableEntityProjector: IEntityProjector {
    fn projector_key(&self) -> &str;
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ProjectionCacheOptions {
    pub maximum_entities: usize,
    pub maximum_occurrences: usize,
}

impl Default for ProjectionCacheOptions {
    fn default() -> Self {
        Self {
            maximum_entities: 65_536,
            maximum_occurrences: 262_144,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct ProjectionCacheStatistics {
    pub projections: i64,
    pub projection_hits: i64,
    pub occurrence_hits: i64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct SceneBuildStatistics {
    pub roots_projected: usize,
    pub roots_reused: usize,
}

/// Source descriptor for text projection.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct CadTextSource {
    pub text: CadText,
    pub normal: Vec3,
    pub rotation: f64,
    pub style_name: String,
}

/// Text projection utility helpers.
pub struct CadTextProjection;

impl CadTextProjection {
    pub fn build_frame(origin: Vec3, normal: Vec3, rotation_radians: f64) -> Affine3 {
        Affine3::move_by(origin)
            * Affine3::object_coordinates(normal)
            * Affine3::rotate_z(rotation_radians)
    }
}

/// Drawing unit conversion factors (millimeters per unit).
pub struct DrawingUnitConversion;

impl DrawingUnitConversion {
    pub fn meters_per_unit(insunits: i32) -> f64 {
        match insunits {
            1 => 0.0254,
            2 => 0.3048,
            3 => 1609.344,
            4 => 0.001,
            5 => 0.01,
            6 => 1.0,
            7 => 1000.0,
            _ => 1.0,
        }
    }
}

/// Standard entity projector handling core DXF shadow entities (Line, Circle, Arc, Polyline, Text, etc.).
#[derive(Debug, Default)]
pub struct StandardEntityProjector;

impl IEntityProjector for StandardEntityProjector {
    fn project(&self, snapshot: &ShadowSnapshot, entity_id: &ObjectId) -> Option<CadGeometry> {
        let reader = snapshot.read(entity_id.clone());
        let entity_type = reader.type_name();
        match entity_type {
            "Line" => {
                let start = reader.field("StartPoint").vector(Vec3::ZERO);
                let end = reader.field("EndPoint").vector(Vec3::ZERO);
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![Curve3::Line(crate::curves::LineCurve { a: start, b: end })],
                        closed: false,
                    }],
                    ..Default::default()
                })
            }
            "Circle" => {
                let center = reader.field("Center").vector(Vec3::ZERO);
                let radius = reader.field("Radius").number(0.0);
                if radius <= 0.0 {
                    return None;
                }
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![crate::curves::CurveSampler::circle(center, radius)],
                        closed: true,
                    }],
                    ..Default::default()
                })
            }
            "Arc" => {
                let center = reader.field("Center").vector(Vec3::ZERO);
                let radius = reader.field("Radius").number(0.0);
                if radius <= 0.0 {
                    return None;
                }
                let start_deg = reader.field("StartAngle").number(0.0);
                let end_deg = reader.field("EndAngle").number(360.0);
                let mut sweep_deg = end_deg - start_deg;
                if sweep_deg <= 0.0 {
                    sweep_deg += 360.0;
                }
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![Curve3::Ellipse(crate::curves::EllipseCurve {
                            center,
                            u: Vec3::new(radius, 0.0, 0.0),
                            v: Vec3::new(0.0, radius, 0.0),
                            start_angle: start_deg.to_radians(),
                            sweep: sweep_deg.to_radians(),
                        })],
                        closed: false,
                    }],
                    ..Default::default()
                })
            }
            "Ellipse" => {
                let center = reader.field("Center").vector(Vec3::ZERO);
                let major = reader.field("MajorAxis").number(0.0) * 0.5;
                let minor = reader.field("MinorAxis").number(0.0) * 0.5;
                if major <= 0.0 || minor <= 0.0 {
                    return None;
                }
                let rot = reader.field("Rotation").number(0.0).to_radians();
                let (s, c) = rot.sin_cos();
                let start_deg = reader.field("StartAngle").number(0.0);
                let end_deg = reader.field("EndAngle").number(360.0);
                let mut sweep_deg = end_deg - start_deg;
                if sweep_deg <= 0.0 {
                    sweep_deg += 360.0;
                }
                Some(CadGeometry {
                    contours: vec![CadContour {
                        curves: vec![Curve3::Ellipse(crate::curves::EllipseCurve {
                            center,
                            u: Vec3::xy(major * c, major * s),
                            v: Vec3::xy(-minor * s, minor * c),
                            start_angle: start_deg.to_radians(),
                            sweep: sweep_deg.to_radians(),
                        })],
                        closed: (sweep_deg - 360.0).abs() < 1e-6,
                    }],
                    ..Default::default()
                })
            }
            "Polyline2D" => {
                let items = reader.field("Vertexes").items();
                let closed = reader.field("IsClosed").boolean(false);
                let elev = reader.field("Elevation").number(0.0);
                if items.len() < 2 {
                    return Some(CadGeometry::default());
                }
                let mut curves = Vec::new();
                let seg_count = if closed { items.len() } else { items.len() - 1 };
                for i in 0..seg_count {
                    let a_r = &items[i];
                    let b_r = &items[(i + 1) % items.len()];
                    let p0 = a_r.field("Position").vector(Vec3::ZERO);
                    let p1 = b_r.field("Position").vector(Vec3::ZERO);
                    let a = Vec3::new(p0.x, p0.y, elev);
                    let b = Vec3::new(p1.x, p1.y, elev);
                    let bulge = a_r.field("Bulge").number(0.0);
                    if bulge.abs() < 1e-9 {
                        curves.push(Curve3::Line(crate::curves::LineCurve { a, b }));
                    } else {
                        let chord = Vec3::distance(a, b);
                        if chord > 1e-12 {
                            let theta = 4.0 * bulge.atan();
                            let half = theta * 0.5;
                            let radius = (chord * 0.5) / half.sin().abs();
                            let mid = (a + b) * 0.5;
                            let dir = (b - a) / chord;
                            let perp = Vec3::xy(-dir.y, dir.x);
                            let sag_offset = radius * half.cos() * bulge.signum();
                            let center = Vec3::new(
                                mid.x + perp.x * sag_offset,
                                mid.y + perp.y * sag_offset,
                                elev,
                            );
                            let start_angle = (a.y - center.y).atan2(a.x - center.x);
                            curves.push(Curve3::Ellipse(crate::curves::EllipseCurve {
                                center,
                                u: Vec3::new(radius, 0.0, 0.0),
                                v: Vec3::new(0.0, radius, 0.0),
                                start_angle,
                                sweep: theta,
                            }));
                        }
                    }
                }
                Some(CadGeometry {
                    contours: vec![CadContour { curves, closed }],
                    ..Default::default()
                })
            }
            "Polyline3D" => {
                let items = reader.field("Vertexes").items();
                let closed = reader.field("IsClosed").boolean(false);
                if items.len() < 2 {
                    return Some(CadGeometry::default());
                }
                let mut curves = Vec::new();
                let seg_count = if closed { items.len() } else { items.len() - 1 };
                for i in 0..seg_count {
                    let a = items[i].vector(Vec3::ZERO);
                    let b = items[(i + 1) % items.len()].vector(Vec3::ZERO);
                    curves.push(Curve3::Line(crate::curves::LineCurve { a, b }));
                }
                Some(CadGeometry {
                    contours: vec![CadContour { curves, closed }],
                    ..Default::default()
                })
            }
            "Solid" | "Face3D" => {
                let v0 = reader.field("FirstVertex").vector(Vec3::ZERO);
                let v1 = reader.field("SecondVertex").vector(Vec3::ZERO);
                let v2 = reader.field("ThirdVertex").vector(Vec3::ZERO);
                let v3 = reader.field("FourthVertex").vector(Vec3::ZERO);
                let curves = vec![
                    Curve3::Line(crate::curves::LineCurve { a: v0, b: v1 }),
                    Curve3::Line(crate::curves::LineCurve { a: v1, b: v2 }),
                    Curve3::Line(crate::curves::LineCurve { a: v2, b: v3 }),
                    Curve3::Line(crate::curves::LineCurve { a: v3, b: v0 }),
                ];
                let contour = CadContour {
                    curves,
                    closed: true,
                };
                Some(CadGeometry {
                    contours: vec![contour.clone()],
                    filled: entity_type == "Solid",
                    regions: if entity_type == "Solid" {
                        vec![CadRegion {
                            contours: vec![contour],
                            fill_rule: CadFillRule::EvenOdd,
                            argb: None,
                            gradient: None,
                        }]
                    } else {
                        Vec::new()
                    },
                    ..Default::default()
                })
            }
            "Text" | "MText" => {
                let origin = reader.field("Position").vector(Vec3::ZERO);
                let height = reader.field("Height").number(2.5);
                let rot = reader.field("Rotation").number(0.0).to_radians();
                let (s, c) = rot.sin_cos();
                Some(CadGeometry {
                    texts: vec![CadText {
                        content: reader.field("Value").text().to_string(),
                        origin,
                        x_axis: Vec3::xy(c, s),
                        y_axis: Vec3::xy(-s, c),
                        height,
                        width_factor: 1.0,
                        oblique_angle: 0.0,
                        font: "Arial".into(),
                        is_mtext: entity_type == "MText",
                        line_spacing: CadLineSpacing::default(),
                    }],
                    points: vec![origin],
                    ..Default::default()
                })
            }
            "Point" => {
                let pos = reader.field("Position").vector(Vec3::ZERO);
                Some(CadGeometry {
                    points: vec![pos],
                    ..Default::default()
                })
            }
            _ => Some(CadGeometry::default()),
        }
    }
}

impl ICacheableEntityProjector for StandardEntityProjector {
    fn projector_key(&self) -> &str {
        "standard"
    }
}

/// Builds a [`CadScene`] from a [`ShadowSnapshot`].
pub struct CadSceneBuilder {
    projectors: Vec<Box<dyn IEntityProjector>>,
    pub cache_options: ProjectionCacheOptions,
    pub cache_stats: ProjectionCacheStatistics,
    pub build_stats: SceneBuildStatistics,
}

impl Default for CadSceneBuilder {
    fn default() -> Self {
        Self::new()
    }
}

impl CadSceneBuilder {
    pub fn new() -> Self {
        Self {
            projectors: vec![Box::new(StandardEntityProjector)],
            cache_options: ProjectionCacheOptions::default(),
            cache_stats: ProjectionCacheStatistics::default(),
            build_stats: SceneBuildStatistics::default(),
        }
    }

    pub fn add_projector(&mut self, projector: Box<dyn IEntityProjector>) {
        self.projectors.push(projector);
    }

    pub fn build(&mut self, snapshot: &ShadowSnapshot, options: &SceneOptions) -> CadScene {
        let mut occurrences = Vec::new();
        let mut scene_bounds = Bounds2::EMPTY;
        let mut seen: BTreeMap<String, usize> = BTreeMap::new();

        for entity_id in &snapshot.entity_ids {
            if occurrences.len() >= options.maximum_occurrences {
                break;
            }
            for projector in self.projectors.iter().rev() {
                if let Some(geom) = projector.project(snapshot, entity_id) {
                    let mut bounds = Bounds2::EMPTY;
                    for pt in &geom.points {
                        bounds = bounds.include(*pt);
                    }
                    for c in &geom.contours {
                        for curve in &c.curves {
                            for p in crate::curves::CurveSampler::sample(
                                curve,
                                &crate::curves::SamplingOptions::default(),
                            ) {
                                bounds = bounds.include(p);
                            }
                        }
                    }
                    scene_bounds = scene_bounds.union(bounds);
                    let key = entity_id.as_str().to_string();
                    seen.insert(key.clone(), occurrences.len());
                    occurrences.push(CadOccurrence {
                        key,
                        entity: entity_id.clone(),
                        insert_path: Vec::new(),
                        transform: Affine3::IDENTITY,
                        style: CadStyle::default(),
                        geometry: geom,
                        bounds,
                        components: Vec::new(),
                    });
                    self.cache_stats.projections += 1;
                    self.build_stats.roots_projected += 1;
                    break;
                }
            }
        }

        CadScene {
            document_id: snapshot.document_id.clone(),
            revision: snapshot.revision,
            layout: options.layout.clone(),
            occurrences,
            bounds: scene_bounds,
        }
    }
}
