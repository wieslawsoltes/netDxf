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

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct TextRunMetrics {
    pub width: f64,
    pub ascent: f64,
    pub descent: f64,
}

impl TextRunMetrics {
    pub fn advance(&self) -> f64 {
        self.width
    }

    pub fn glyph_bounds(&self) -> Bounds2 {
        Bounds2::new(0.0, -self.descent, self.width, self.ascent)
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct PositionedTextRun {
    pub text: String,
    pub origin: Vec3,
    pub x: f64,
    pub y: f64,
    pub advance: f64,
    pub style: RichTextStyle,
    pub metrics: TextRunMetrics,
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct TextRule {
    pub start: Vec3,
    pub end: Vec3,
    pub x1: f64,
    pub y1: f64,
    pub x2: f64,
    pub y2: f64,
    pub color: Option<u32>,
    pub thickness: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextLine {
    pub runs: Vec<PositionedTextRun>,
    pub rules: Vec<TextRule>,
    pub width: f64,
    pub height: f64,
    pub left: f64,
    pub right: f64,
    pub baseline: f64,
    pub line: usize,
    pub paragraph: usize,
    pub is_paragraph_end: bool,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextLayoutResult {
    pub lines: Vec<RichTextLine>,
    pub runs: Vec<PositionedTextRun>,
    pub rules: Vec<TextRule>,
    pub bounds: Bounds2,
    pub layout_bounds: Bounds2,
    pub diagnostics: Vec<TextFormatDiagnostic>,
    pub total_width: f64,
    pub total_height: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextLayout {
    pub lines: Vec<RichTextLine>,
    pub boxes: Vec<Bounds2>,
    pub left: f64,
    pub right: f64,
    pub top: f64,
    pub bottom: f64,
    pub ascent: f64,
    pub descent: f64,
    pub baseline: f64,
    pub nominal_height: f64,
    pub number: usize,
    pub paragraph: usize,
    pub style: RichTextStyle,
    pub cursor: Vec3,
    pub end: Vec3,
    pub has_content: bool,
    pub has_tab: bool,
    pub approximate_metrics: bool,
}

impl RichTextLayout {
    pub fn layout(
        doc: &RichTextDocument,
        engine: &dyn ITextLayoutEngine,
        line_spacing: f64,
    ) -> RichTextLayoutResult {
        let mut lines: Vec<RichTextLine> = vec![RichTextLine::default()];
        let mut cur_x = 0.0;
        let mut cur_y = 0.0;

        for inline in &doc.inlines {
            for (seg_idx, segment) in inline.text.split('\n').enumerate() {
                if seg_idx > 0 {
                    let prev_h = lines.last().map_or(inline.style.height, |l| l.height.max(inline.style.height));
                    cur_y -= prev_h * line_spacing.max(0.5);
                    cur_x = 0.0;
                    lines.push(RichTextLine::default());
                }
                if segment.is_empty() {
                    continue;
                }
                let metrics = engine.measure_run(segment, &inline.style);
                let line = lines.last_mut().unwrap();
                line.runs.push(PositionedTextRun {
                    text: segment.to_string(),
                    origin: Vec3::xy(cur_x, cur_y),
                    x: cur_x,
                    y: cur_y,
                    advance: metrics.width,
                    style: inline.style.clone(),
                    metrics,
                });
                cur_x += metrics.width;
                line.width = cur_x;
                line.height = line.height.max(metrics.ascent + metrics.descent);
            }
        }

        let total_width = lines.iter().map(|l| l.width).fold(0.0_f64, f64::max);
        let total_height: f64 = lines.iter().map(|l| l.height).sum();
        let all_runs: Vec<PositionedTextRun> =
            lines.iter().flat_map(|l| l.runs.iter().cloned()).collect();
        let all_rules: Vec<TextRule> =
            lines.iter().flat_map(|l| l.rules.iter().copied()).collect();
        let bounds = Bounds2::new(0.0, -total_height, total_width, 0.0);
        RichTextLayoutResult {
            lines,
            runs: all_runs,
            rules: all_rules,
            bounds,
            layout_bounds: bounds,
            diagnostics: doc.diagnostics.clone(),
            total_width,
            total_height,
        }
    }
}

