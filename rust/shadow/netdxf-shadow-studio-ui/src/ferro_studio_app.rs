#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::Vec3;
#[allow(unused_imports)]
use netdxf_shadow_rendering::{IRenderBackend, RenderFrame, RenderImage, Viewport};
#[allow(unused_imports)]
use netdxf_shadow_rendering_skia::SkiaRenderBackend;
#[allow(unused_imports)]
use netdxf_shadow_studio_core::Studio;
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};

/// FerroUI Studio view-model state driving viewport pan/zoom, panels, and Skia rendering.
pub struct FerroStudioApp {
    pub studio: Studio,
    pub viewport: Viewport,
    pub background_argb: u32,
    pub active_panels: Vec<FerroStudioPanel>,
    pub backend: SkiaRenderBackend,
}

impl FerroStudioApp {
    pub fn new(sample_name: &str) -> Self {
        Self {
            studio: Studio::from_sample("doc-1", sample_name),
            viewport: Viewport::default(),
            background_argb: 0xFF18_1A1F,
            active_panels: vec![
                FerroStudioPanel::Viewport,
                FerroStudioPanel::Layers,
                FerroStudioPanel::Properties,
                FerroStudioPanel::AgentChat,
            ],
            backend: SkiaRenderBackend::new(),
        }
    }

    pub fn pan(&mut self, dx: f64, dy: f64) {
        self.viewport.center = Vec3::xy(
            self.viewport.center.x - dx / self.viewport.zoom,
            self.viewport.center.y + dy / self.viewport.zoom,
        );
    }

    pub fn zoom_by(&mut self, factor: f64) {
        self.viewport.zoom = (self.viewport.zoom * factor).clamp(1e-4, 1e6);
    }

    pub fn zoom_to_fit(&mut self, layout: &str, margin_ratio: f64) {
        let scene = self.studio.build_cad_scene(layout);
        if scene.bounds.is_empty() {
            return;
        }
        self.viewport.center = scene.bounds.center();
        let w = scene.bounds.width().max(1e-6);
        let h = scene.bounds.height().max(1e-6);
        let scale = (1.0 - margin_ratio.clamp(0.0, 0.4)).max(0.1);
        let zx = (self.viewport.width * scale) / w;
        let zy = (self.viewport.height * scale) / h;
        self.viewport.zoom = zx.min(zy).clamp(1e-4, 1e6);
    }

    pub fn pick(&mut self, layout: &str, screen_x: f64, screen_y: f64, pixel_tolerance: f64) -> Vec<netdxf_shadow::ObjectId> {
        let world = self.viewport.screen_to_world(Vec3::xy(screen_x, screen_y));
        let world_tol = (pixel_tolerance / self.viewport.zoom.max(1e-6)).max(1e-6);
        let render_scene = self.studio.compile_render_scene(layout);
        netdxf_shadow_rendering::RenderHitTesting::hit_test(&render_scene, world, world_tol)
    }

    pub fn render_viewport(&mut self, layout: &str) -> Result<RenderImage, String> {
        let scene = self.studio.compile_render_scene(layout);
        let frame = RenderFrame {
            viewport: self.viewport,
            background_argb: self.background_argb,
            device_pixel_ratio: 1.0,
        };
        self.backend.render(&scene, &frame)
    }
}

