use netdxf_shadow::{Affine3, Bounds2, ObjectId, Vec3};
use netdxf_shadow_geometry::{CadFillRule, CadGradient, CadScene, CurveSampler, SamplingOptions};
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct GradientPlan {
    pub kind: String,
    pub color1: u32,
    pub color2: u32,
    pub start: Vec3,
    pub end: Vec3,
    pub shift: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct StrokePattern {
    pub dashes: Vec<f64>,
    pub phase: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderPaint {
    pub argb: u32,
    pub lineweight: f64,
    pub pattern: Option<StrokePattern>,
    pub gradient: Option<GradientPlan>,
}

impl Default for RenderPaint {
    fn default() -> Self {
        Self {
            argb: 0xFFFF_FFFF,
            lineweight: 0.25,
            pattern: None,
            gradient: None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RenderContour {
    pub points: Vec<Vec3>,
    pub closed: bool,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderRegion {
    pub contours: Vec<RenderContour>,
    pub fill_rule: CadFillRule,
    pub argb: Option<u32>,
    pub gradient: Option<CadGradient>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderStrokeGroup {
    pub contours: Vec<RenderContour>,
    pub argb: Option<u32>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderClip {
    pub bounds: Bounds2,
    pub polygon: Vec<Vec3>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderItem {
    pub key: String,
    pub entity: ObjectId,
    pub layer: String,
    pub paint: RenderPaint,
    pub contours: Vec<RenderContour>,
    pub regions: Vec<RenderRegion>,
    pub stroke_groups: Vec<RenderStrokeGroup>,
    pub bounds: Bounds2,
    pub clip: Option<RenderClip>,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderScene {
    pub document_id: String,
    pub revision: i64,
    pub layout: String,
    pub items: Vec<RenderItem>,
    pub bounds: Bounds2,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct RenderCompileOptions {
    pub curve_tolerance: f64,
    pub maximum_items: usize,
    pub background_argb: u32,
}

impl Default for RenderCompileOptions {
    fn default() -> Self {
        Self {
            curve_tolerance: 0.05,
            maximum_items: 1_000_000,
            background_argb: 0xFF18_1A1F,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct RenderCompilerStatistics {
    pub items_compiled: usize,
    pub items_reused: usize,
}

/// Compiles a [`CadScene`] into a flat backend-agnostic [`RenderScene`].
#[derive(Debug, Default)]
pub struct RenderCompiler {
    pub statistics: RenderCompilerStatistics,
}

impl RenderCompiler {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn compile(&mut self, scene: &CadScene, options: &RenderCompileOptions) -> RenderScene {
        let sample_opts = SamplingOptions {
            tolerance: options.curve_tolerance,
            ..Default::default()
        };
        let mut items = Vec::with_capacity(scene.occurrences.len().min(options.maximum_items));
        for occ in scene.occurrences.iter().take(options.maximum_items) {
            let mut contours = Vec::new();
            for c in &occ.geometry.contours {
                let mut pts = Vec::new();
                for curve in &c.curves {
                    let sampled = CurveSampler::sample(curve, &sample_opts);
                    if !pts.is_empty() && !sampled.is_empty() && pts.last() == sampled.first() {
                        pts.extend_from_slice(&sampled[1..]);
                    } else {
                        pts.extend(sampled);
                    }
                }
                if !pts.is_empty() {
                    contours.push(RenderContour {
                        points: pts,
                        closed: c.closed,
                    });
                }
            }
            items.push(RenderItem {
                key: occ.key.clone(),
                entity: occ.entity.clone(),
                layer: occ.style.layer.clone(),
                paint: RenderPaint {
                    argb: occ.style.argb,
                    lineweight: occ.style.lineweight,
                    pattern: (!occ.style.dashes.is_empty()).then(|| StrokePattern {
                        dashes: occ.style.dashes.clone(),
                        phase: 0.0,
                    }),
                    gradient: None,
                },
                contours,
                regions: Vec::new(),
                stroke_groups: Vec::new(),
                bounds: occ.bounds,
                clip: None,
            });
            self.statistics.items_compiled += 1;
        }

        RenderScene {
            document_id: scene.document_id.clone(),
            revision: scene.revision,
            layout: scene.layout.clone(),
            items,
            bounds: scene.bounds,
        }
    }
}

/// Hit-testing utilities against a compiled [`RenderScene`].
pub struct RenderHitTesting;

impl RenderHitTesting {
    pub fn hit_test(scene: &RenderScene, point: Vec3, tolerance: f64) -> Vec<ObjectId> {
        let box2 = Bounds2::new(
            point.x - tolerance,
            point.y - tolerance,
            point.x + tolerance,
            point.y + tolerance,
        );
        scene
            .items
            .iter()
            .filter(|item| item.bounds.intersects(box2))
            .map(|item| item.entity.clone())
            .collect()
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct Viewport {
    pub width: f64,
    pub height: f64,
    pub center: Vec3,
    pub zoom: f64,
    pub rotation: f64,
}

impl Default for Viewport {
    fn default() -> Self {
        Self {
            width: 1280.0,
            height: 720.0,
            center: Vec3::ZERO,
            zoom: 1.0,
            rotation: 0.0,
        }
    }
}

impl Viewport {
    pub fn world_to_screen(&self, p: Vec3) -> Vec3 {
        let dx = (p.x - self.center.x) * self.zoom + self.width * 0.5;
        let dy = self.height * 0.5 - (p.y - self.center.y) * self.zoom;
        Vec3::xy(dx, dy)
    }

    pub fn screen_to_world(&self, p: Vec3) -> Vec3 {
        let x = (p.x - self.width * 0.5) / self.zoom + self.center.x;
        let y = (self.height * 0.5 - p.y) / self.zoom + self.center.y;
        Vec3::xy(x, y)
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RenderFrame {
    pub viewport: Viewport,
    pub background_argb: u32,
    pub device_pixel_ratio: f64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RenderImage {
    pub width: u32,
    pub height: u32,
    pub format: String,
    pub bytes: Vec<u8>,
}

pub trait IRenderBackend: Send + Sync {
    fn render(&self, scene: &RenderScene, frame: &RenderFrame) -> Result<RenderImage, String>;
}

pub trait IRasterRenderBackend: IRenderBackend {
    fn render_rgba8(&self, scene: &RenderScene, frame: &RenderFrame) -> Result<Vec<u8>, String>;
}

pub struct RenderHost {
    backend: Box<dyn IRenderBackend>,
}

impl RenderHost {
    pub fn new(backend: Box<dyn IRenderBackend>) -> Self {
        Self { backend }
    }

    pub fn render(&self, scene: &RenderScene, frame: &RenderFrame) -> Result<RenderImage, String> {
        self.backend.render(scene, frame)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SvgImageData {
    pub mime_type: String,
    pub bytes: Vec<u8>,
}

pub trait ISvgImageResolver: Send + Sync {
    fn resolve(&self, file_name: &str) -> Option<SvgImageData>;
}

/// Pure Rust SVG render backend (`SvgRenderBackend`).
#[derive(Debug, Default)]
pub struct SvgRenderBackend;

impl SvgRenderBackend {
    pub fn render_svg_string(&self, scene: &RenderScene, frame: &RenderFrame) -> String {
        let mut svg = format!(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{}\" height=\"{}\" viewBox=\"0 0 {} {}\">\n",
            frame.viewport.width, frame.viewport.height, frame.viewport.width, frame.viewport.height
        );
        let bg = frame.background_argb & 0x00FF_FFFF;
        svg.push_str(&format!(
            "  <rect width=\"100%\" height=\"100%\" fill=\"#{bg:06x}\"/>\n"
        ));
        for item in &scene.items {
            let rgb = item.paint.argb & 0x00FF_FFFF;
            let sw = item.paint.lineweight.max(0.5);
            for c in &item.contours {
                if c.points.is_empty() {
                    continue;
                }
                let mut d = String::new();
                for (i, &p) in c.points.iter().enumerate() {
                    let s = frame.viewport.world_to_screen(p);
                    if i == 0 {
                        d.push_str(&format!("M{:.2},{:.2}", s.x, s.y));
                    } else {
                        d.push_str(&format!(" L{:.2},{:.2}", s.x, s.y));
                    }
                }
                if c.closed {
                    d.push_str(" Z");
                }
                svg.push_str(&format!(
                    "  <path d=\"{d}\" fill=\"none\" stroke=\"#{rgb:06x}\" stroke-width=\"{sw:.2}\"/>\n"
                ));
            }
        }
        svg.push_str("</svg>\n");
        svg
    }
}

impl IRenderBackend for SvgRenderBackend {
    fn render(&self, scene: &RenderScene, frame: &RenderFrame) -> Result<RenderImage, String> {
        let svg = self.render_svg_string(scene, frame);
        Ok(RenderImage {
            width: frame.viewport.width.max(1.0) as u32,
            height: frame.viewport.height.max(1.0) as u32,
            format: "image/svg+xml".into(),
            bytes: svg.into_bytes(),
        })
    }
}

// Camera, paper space, annotation, linetype, and viewport layer overrides
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum PerspectiveFieldOfView {
    #[default]
    Vertical,
    Horizontal,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct PerspectiveOptions {
    pub fov_degrees: f64,
    pub fov_mode: PerspectiveFieldOfView,
    pub near_plane: f64,
    pub far_plane: f64,
}

impl Default for PerspectiveOptions {
    fn default() -> Self {
        Self {
            fov_degrees: 45.0,
            fov_mode: PerspectiveFieldOfView::Vertical,
            near_plane: 0.1,
            far_plane: 100_000.0,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct PickingRay {
    pub origin: Vec3,
    pub direction: Vec3,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct PerspectiveCamera {
    pub eye: Vec3,
    pub target: Vec3,
    pub up: Vec3,
    pub options: PerspectiveOptions,
}

impl PerspectiveCamera {
    pub fn forward(self) -> Vec3 {
        (self.target - self.eye).normalized()
    }

    pub fn right(self) -> Vec3 {
        Vec3::cross(self.forward(), self.up).normalized()
    }

    pub fn camera_up(self) -> Vec3 {
        Vec3::cross(self.right(), self.forward()).normalized()
    }

    pub fn project_point(self, world: Vec3, width: f64, height: f64) -> Option<Vec3> {
        let rel = world - self.eye;
        let z = Vec3::dot(rel, self.forward());
        if z <= self.options.near_plane || z >= self.options.far_plane {
            return None;
        }
        let x = Vec3::dot(rel, self.right());
        let y = Vec3::dot(rel, self.camera_up());
        let aspect = (width / height.max(1.0)).max(1e-6);
        let half_tan = (self.options.fov_degrees.to_radians() * 0.5).tan();
        let (tan_x, tan_y) = match self.options.fov_mode {
            PerspectiveFieldOfView::Vertical => (half_tan * aspect, half_tan),
            PerspectiveFieldOfView::Horizontal => (half_tan, half_tan / aspect),
        };
        let ndc_x = x / (z * tan_x);
        let ndc_y = y / (z * tan_y);
        Some(Vec3::new(
            (ndc_x + 1.0) * 0.5 * width,
            (1.0 - ndc_y) * 0.5 * height,
            z,
        ))
    }

    pub fn unproject_ray(self, screen_x: f64, screen_y: f64, width: f64, height: f64) -> PickingRay {
        let aspect = (width / height.max(1.0)).max(1e-6);
        let half_tan = (self.options.fov_degrees.to_radians() * 0.5).tan();
        let (tan_x, tan_y) = match self.options.fov_mode {
            PerspectiveFieldOfView::Vertical => (half_tan * aspect, half_tan),
            PerspectiveFieldOfView::Horizontal => (half_tan, half_tan / aspect),
        };
        let ndc_x = (screen_x / width.max(1.0)) * 2.0 - 1.0;
        let ndc_y = 1.0 - (screen_y / height.max(1.0)) * 2.0;
        let dir = (self.forward()
            + self.right() * (ndc_x * tan_x)
            + self.camera_up() * (ndc_y * tan_y))
            .normalized();
        PickingRay {
            origin: self.eye,
            direction: dir,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PaperViewportProjection {
    pub transform: Affine3,
    pub clip: RenderClip,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
pub struct PaperSpaceOptions {
    pub include_viewport_borders: bool,
    pub respect_layer_overrides: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ModelAnnotationMode {
    #[default]
    Auto,
    ShowAll,
    MatchCurrentScale,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct AnnotationScale {
    pub paper_units: f64,
    pub drawing_units: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ModelAnnotationContext {
    pub mode: ModelAnnotationMode,
    pub current_scale: AnnotationScale,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct ModelAnnotationResolution {
    pub visible: bool,
    pub scale_factor: f64,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum LinetypeMetricSpace {
    #[default]
    World,
    Paper,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum LinetypeTextRotationPivot {
    #[default]
    BaselineOrigin,
    GlyphCenter,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum LinetypeFrameMode {
    #[default]
    Planar,
    ParallelTransport,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypePlanOptions {
    pub metric_space: LinetypeMetricSpace,
    pub rotation_pivot: LinetypeTextRotationPivot,
    pub frame_mode: LinetypeFrameMode,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct LinetypePlacement {
    pub distance: f64,
    pub position: Vec3,
    pub tangent: Vec3,
}

pub struct LinetypePlanner;

impl LinetypePlanner {
    /// Walks a polyline by arc length and emits placements every `spacing` units.
    pub fn plan(points: &[Vec3], spacing: f64) -> Vec<LinetypePlacement> {
        if points.len() < 2 || spacing <= 1e-9 {
            return Vec::new();
        }
        let mut placements = Vec::new();
        let mut accum = 0.0;
        let mut next_target = spacing;
        for w in points.windows(2) {
            let seg = w[1] - w[0];
            let len = seg.length();
            if len <= 1e-12 {
                continue;
            }
            let tangent = seg / len;
            while next_target <= accum + len && placements.len() < 100_000 {
                let local = next_target - accum;
                placements.push(LinetypePlacement {
                    distance: next_target,
                    position: w[0] + tangent * local,
                    tangent,
                });
                next_target += spacing;
            }
            accum += len;
        }
        placements
    }
}

pub struct LinetypeTransport;

impl LinetypeTransport {
    /// Computes orthonormal tangent/normal/binormal frames along a polyline.
    pub fn compute_frames(points: &[Vec3], normal_hint: Vec3) -> Vec<(Vec3, Vec3, Vec3)> {
        if points.len() < 2 {
            return Vec::new();
        }
        let n = normal_hint.normalized();
        let mut frames = Vec::with_capacity(points.len());
        for i in 0..points.len() {
            let t = if i + 1 < points.len() {
                (points[i + 1] - points[i]).normalized()
            } else {
                (points[i] - points[i - 1]).normalized()
            };
            let b = Vec3::cross(n, t).normalized();
            frames.push((t, b, n));
        }
        frames
    }
}

pub trait ILinetypeDecorationProvider: Send + Sync {
    fn resolve_decoration(&self, name: &str) -> Option<Vec<RenderContour>>;
}

pub trait ITextOutlineProvider: Send + Sync {
    fn text_outlines(&self, text: &str, font: &str, height: f64) -> Vec<RenderContour>;
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ViewportLayerOverride {
    pub layer: String,
    pub frozen: Option<bool>,
    pub argb: Option<u32>,
    pub lineweight: Option<f64>,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct ViewportLayerProfile {
    pub overrides: BTreeMap<String, ViewportLayerOverride>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct ViewportOverrideLimits {
    pub maximum_viewports: usize,
    pub maximum_overrides_per_viewport: usize,
}

impl Default for ViewportOverrideLimits {
    fn default() -> Self {
        Self {
            maximum_viewports: 1_024,
            maximum_overrides_per_viewport: 8_192,
        }
    }
}

#[derive(Debug, Clone, Default)]
pub struct ViewportOverrideCatalog {
    pub profiles: BTreeMap<ObjectId, ViewportLayerProfile>,
}
