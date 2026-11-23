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

