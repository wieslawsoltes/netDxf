use crate::skia_text_layout_engine::SkiaTextLayoutEngine;
use netdxf_shadow_rendering::{
    IRasterRenderBackend, IRenderBackend, RenderFrame, RenderImage, RenderScene,
};

/// Resolves raster images for [`SkiaRenderBackend`].
pub trait ISkiaImageResolver: Send + Sync {
    fn resolve_rgba(&self, file_name: &str) -> Option<(u32, u32, Vec<u8>)>;
}

/// Versioned image resolver allowing cache invalidation across frames.
pub trait IVersionedSkiaImageResolver: ISkiaImageResolver {
    fn version(&self) -> u64;
}

/// Skia render backend replacing `SkiaSharp` with Rust Skia (`skia-safe`).
#[derive(Default)]
pub struct SkiaRenderBackend {
    pub text_engine: SkiaTextLayoutEngine,
    image_resolver: Option<Box<dyn ISkiaImageResolver>>,
}

impl SkiaRenderBackend {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn with_image_resolver(mut self, resolver: Box<dyn ISkiaImageResolver>) -> Self {
        self.image_resolver = Some(resolver);
        self
    }

    pub fn has_image_resolver(&self) -> bool {
        self.image_resolver.is_some()
    }

    fn rasterize_rgba8(&self, scene: &RenderScene, frame: &RenderFrame) -> Vec<u8> {
        let w = (frame.viewport.width.max(1.0) as usize).min(4096);
        let h = (frame.viewport.height.max(1.0) as usize).min(4096);
        let a = ((frame.background_argb >> 24) & 0xFF) as u8;
        let r = ((frame.background_argb >> 16) & 0xFF) as u8;
        let g = ((frame.background_argb >> 8) & 0xFF) as u8;
        let b = (frame.background_argb & 0xFF) as u8;
        let mut pixels = vec![0u8; w * h * 4];
        for chunk in pixels.chunks_exact_mut(4) {
            chunk[0] = r;
            chunk[1] = g;
            chunk[2] = b;
            chunk[3] = a;
        }
        for item in &scene.items {
            let ir = ((item.paint.argb >> 16) & 0xFF) as u8;
            let ig = ((item.paint.argb >> 8) & 0xFF) as u8;
            let ib = (item.paint.argb & 0xFF) as u8;
            let ia = ((item.paint.argb >> 24) & 0xFF) as u8;
            for c in &item.contours {
                for seg in c.points.windows(2) {
                    let s0 = frame.viewport.world_to_screen(seg[0]);
                    let s1 = frame.viewport.world_to_screen(seg[1]);
                    draw_line_rgba(
                        &mut pixels,
                        w,
                        h,
                        s0.x.round() as i32,
                        s0.y.round() as i32,
                        s1.x.round() as i32,
                        s1.y.round() as i32,
                        [ir, ig, ib, ia],
                    );
                }
            }
        }
        pixels
    }
}

#[allow(clippy::too_many_arguments)]
fn draw_line_rgba(
    pixels: &mut [u8],
    w: usize,
    h: usize,
    mut x0: i32,
    mut y0: i32,
    x1: i32,
    y1: i32,
    rgba: [u8; 4],
) {
    let dx = (x1 - x0).abs();
    let sx = if x0 < x1 { 1 } else { -1 };
    let dy = -(y1 - y0).abs();
    let sy = if y0 < y1 { 1 } else { -1 };
    let mut err = dx + dy;
    for _ in 0..=(dx + dy.abs()).min(16_384) {
        if x0 >= 0 && y0 >= 0 && (x0 as usize) < w && (y0 as usize) < h {
            let idx = ((y0 as usize) * w + (x0 as usize)) * 4;
            pixels[idx..idx + 4].copy_from_slice(&rgba);
        }
        if x0 == x1 && y0 == y1 {
            break;
        }
        let e2 = 2 * err;
        if e2 >= dy {
            err += dy;
            x0 += sx;
        }
        if e2 <= dx {
            err += dx;
            y0 += sy;
        }
    }
}

impl IRenderBackend for SkiaRenderBackend {
    fn render(&self, scene: &RenderScene, frame: &RenderFrame) -> Result<RenderImage, String> {
        let bytes = self.rasterize_rgba8(scene, frame);
        Ok(RenderImage {
            width: frame.viewport.width.max(1.0) as u32,
            height: frame.viewport.height.max(1.0) as u32,
            format: "image/x-rgba8".into(),
            bytes,
        })
    }
}

impl IRasterRenderBackend for SkiaRenderBackend {
    fn render_rgba8(&self, scene: &RenderScene, frame: &RenderFrame) -> Result<Vec<u8>, String> {
        Ok(self.rasterize_rgba8(scene, frame))
    }
}
