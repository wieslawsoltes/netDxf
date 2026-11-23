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

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct TextLine {
    pub text: String,
    pub width: f64,
    pub x: f64,
    pub y: f64,
}

pub struct TextLayout;

impl TextLayout {
    pub fn plain_text(text: &str) -> String {
        text.to_string()
    }

    pub fn layout(
        text: &str,
        style: &RichTextStyle,
        max_width: f64,
        engine: &dyn ITextLayoutEngine,
    ) -> Vec<TextLine> {
        Self::wrap_lines(text, style, max_width, engine)
    }
    /// Wraps plain text into lines fitting within `max_width` using `engine`.
    pub fn wrap_lines(
        text: &str,
        style: &RichTextStyle,
        max_width: f64,
        engine: &dyn ITextLayoutEngine,
    ) -> Vec<TextLine> {
        let mut out = Vec::new();
        for paragraph in text.split('\n') {
            if max_width <= 0.0 {
                let w = engine.measure_run(paragraph, style).width;
                out.push(TextLine {
                    text: paragraph.to_string(),
                    width: w,
                    ..Default::default()
                });
                continue;
            }
            let mut current = String::new();
            for word in paragraph.split_whitespace() {
                let candidate = if current.is_empty() {
                    word.to_string()
                } else {
                    format!("{current} {word}")
                };
                if engine.measure_run(&candidate, style).width <= max_width || current.is_empty() {
                    current = candidate;
                } else {
                    let w = engine.measure_run(&current, style).width;
                    out.push(TextLine {
                        text: current,
                        width: w,
                        ..Default::default()
                    });
                    current = word.to_string();
                }
            }
            let w = engine.measure_run(&current, style).width;
            out.push(TextLine {
                text: current,
                width: w,
                ..Default::default()
            });
        }
        out
    }
}

