#[allow(unused_imports)]
use crate::*;
#[allow(unused_imports)]
use netdxf_shadow::{Affine3, Bounds2, ObjectId, Vec3};
#[allow(unused_imports)]
use netdxf_shadow_geometry::{CadFillRule, CadGradient, CadScene, CurveSampler, SamplingOptions};
#[allow(unused_imports)]
use serde::{Deserialize, Serialize};
#[allow(unused_imports)]
use std::collections::BTreeMap;

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
    pub fn pixels_per_unit(&self) -> f64 {
        self.zoom
    }

    pub fn to_screen(&self, p: Vec3) -> Vec3 {
        self.world_to_screen(p)
    }

    pub fn to_world(&self, p: Vec3) -> Vec3 {
        self.screen_to_world(p)
    }

    pub fn world_bounds(&self) -> Bounds2 {
        let tl = self.screen_to_world(Vec3::xy(0.0, 0.0));
        let br = self.screen_to_world(Vec3::xy(self.width, self.height));
        Bounds2::new(
            tl.x.min(br.x),
            tl.y.min(br.y),
            tl.x.max(br.x),
            tl.y.max(br.y),
        )
    }

    pub fn validate(&self) -> Result<(), String> {
        if !self.width.is_finite()
            || !self.height.is_finite()
            || self.width <= 0.0
            || self.height <= 0.0
            || !self.zoom.is_finite()
            || self.zoom <= 0.0
        {
            return Err("Viewport dimensions and zoom must be positive and finite.".into());
        }
        Ok(())
    }

    pub fn fit(&mut self, bounds: Bounds2, margin_ratio: f64) {
        if bounds.is_empty() {
            return;
        }
        self.center = bounds.center();
        let w = bounds.width().max(1e-9);
        let h = bounds.height().max(1e-9);
        let scale = (1.0 - margin_ratio.clamp(0.0, 0.45)).max(0.1);
        self.zoom = ((self.width * scale) / w)
            .min((self.height * scale) / h)
            .clamp(1e-6, 1e6);
    }

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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RenderFrame {
    pub viewport: Viewport,
    pub background_argb: u32,
    pub background: u32,
    pub selection_color: u32,
    pub device_pixel_ratio: f64,
    pub scene: Option<RenderScene>,
    pub selection: Vec<String>,
    pub visible_items: Vec<RenderItem>,
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

