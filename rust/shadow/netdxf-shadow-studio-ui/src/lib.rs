//! FerroUI-based CAD Studio UI shell replacing `netDxf.Shadow.Studio.Web`.
//!
//! Designed to integrate directly with FerroUI (`https://github.com/wieslawsoltes/FerroUI/`)
//! and render the CAD viewport via `SkiaRenderBackend` (`netdxf-shadow-rendering-skia`).

#![forbid(unsafe_code)]

use netdxf_shadow::Vec3;
use netdxf_shadow_rendering::{
    IRenderBackend, RenderFrame, RenderImage, Viewport,
};
use netdxf_shadow_rendering_skia::SkiaRenderBackend;
use netdxf_shadow_studio_core::Studio;
use serde::{Deserialize, Serialize};

/// Static asset descriptors matching `netDxf.Shadow.Studio.Web.StudioWebAssets`.
pub struct StudioWebAssets;

impl StudioWebAssets {
    pub const APP_TITLE: &'static str = "netDxf Shadow Studio (FerroUI + Skia)";
    pub const UI_FRAMEWORK: &'static str = "FerroUI";
    pub const RENDER_BACKEND: &'static str = "Skia";
}

/// FerroUI dockable panel identifiers for the CAD Studio workspace.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub enum FerroStudioPanel {
    Viewport,
    Layers,
    Properties,
    Symbols,
    Health,
    AgentChat,
}

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
