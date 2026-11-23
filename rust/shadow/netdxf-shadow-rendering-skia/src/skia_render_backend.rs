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

    pub fn resolve_image(&self, file_name: &str) -> Option<(u32, u32, Vec<u8>)> {
        self.image_resolver
            .as_ref()
            .and_then(|r| r.resolve_rgba(file_name))
    }

    /// Renders `scene` into an SVG document string with full stroke color, lineweight, and polylines.
    pub fn render_svg(&self, scene: &RenderScene, frame: &RenderFrame) -> String {
        let w = frame.viewport.width.max(1.0);
        let h = frame.viewport.height.max(1.0);
        let bg = frame.background_argb & 0x00FF_FFFF;
        let mut svg = format!(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">\n<rect width=\"100%\" height=\"100%\" fill=\"#{bg:06X}\"/>\n"
        );
        for item in &scene.items {
            let rgb = item.paint.argb & 0x00FF_FFFF;
            let sw = item.paint.lineweight.max(0.5);
            for c in &item.contours {
                if c.points.len() < 2 {
                    continue;
                }
                let tag = if c.closed { "polygon" } else { "polyline" };
                svg.push_str(&format!(
                    "<{tag} fill=\"none\" stroke=\"#{rgb:06X}\" stroke-width=\"{sw:.2}\" points=\""
                ));
                for (i, &p) in c.points.iter().enumerate() {
                    let s = frame.viewport.world_to_screen(p);
                    if i > 0 {
                        svg.push(' ');
                    }
                    svg.push_str(&format!("{:.2},{:.2}", s.x, s.y));
                }
                svg.push_str("\"/>\n");
            }
        }
        svg.push_str("</svg>\n");
        svg
    }

    pub fn render_to_rgba(&self, scene: &RenderScene, frame: &RenderFrame) -> Vec<u8> {
        self.rasterize_rgba8(scene, frame)
    }

    pub fn dispose(&mut self) {
        self.image_resolver = None;
        self.text_engine.clear_cache();
    }

    fn rasterize_rgba8(&self, scene: &RenderScene, frame: &RenderFrame) -> Vec<u8> {
        #[cfg(feature = "skia-native")]
        {
            if let Some(bytes) = self.rasterize_with_skia_native(scene, frame) {
                return bytes;
            }
        }
        self.rasterize_software_rgba8(scene, frame)
    }

    #[cfg(feature = "skia-native")]
    fn rasterize_with_skia_native(&self, scene: &RenderScene, frame: &RenderFrame) -> Option<Vec<u8>> {
        use skia_safe::{surfaces, Color, ColorType, ImageInfo, Paint, PaintStyle, Path, AlphaType};
        let w = (frame.viewport.width.max(1.0) as i32).min(4096);
        let h = (frame.viewport.height.max(1.0) as i32).min(4096);
        let info = ImageInfo::new((w, h), ColorType::RGBA8888, AlphaType::Premul, None);
        let mut surface = surfaces::raster(&info, None, None)?;
        let canvas = surface.canvas();
        canvas.clear(Color::new(frame.background_argb));
        for item in &scene.items {
            let mut paint = Paint::default();
            paint.set_anti_alias(true);
            paint.set_color(Color::new(item.paint.argb));
            paint.set_style(PaintStyle::Stroke);
            paint.set_stroke_width(item.paint.lineweight.max(0.5) as f32);
            for c in &item.contours {
                if c.points.len() < 2 {
                    continue;
                }
                let mut path = Path::new();
                let s0 = frame.viewport.world_to_screen(c.points[0]);
                path.move_to((s0.x as f32, s0.y as f32));
                for &p in &c.points[1..] {
                    let s = frame.viewport.world_to_screen(p);
                    path.line_to((s.x as f32, s.y as f32));
                }
                if c.closed {
                    path.close();
                }
                canvas.draw_path(&path, &paint);
            }
        }
        let mut pixels = vec![0u8; (w as usize) * (h as usize) * 4];
        let row_bytes = (w as usize) * 4;
        if surface.read_pixels(&info, &mut pixels, row_bytes, (0, 0)) {
            Some(pixels)
        } else {
            None
        }
    }

    fn rasterize_software_rgba8(&self, scene: &RenderScene, frame: &RenderFrame) -> Vec<u8> {
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
