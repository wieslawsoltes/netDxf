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

pub trait ITextLayoutEngine: Send + Sync {
    fn measure_run(&self, text: &str, style: &RichTextStyle) -> TextRunMetrics;

    fn revision(&self) -> i64 {
        0
    }

    fn layout(&self, _doc: &RichTextDocument, _line_spacing: f64) -> RichTextLayoutResult {
        RichTextLayoutResult::default()
    }
}

#[derive(Debug, Default, Clone, Copy)]
pub struct ApproximateTextLayoutEngine;

impl ApproximateTextLayoutEngine {
    pub const INSTANCE: Self = Self;

    #[inline]
    pub fn revision(&self) -> i64 {
        0
    }

    pub fn layout(&self, doc: &RichTextDocument, line_spacing: f64) -> RichTextLayoutResult {
        RichTextLayout::layout(doc, self, line_spacing)
    }
}

impl ITextLayoutEngine for ApproximateTextLayoutEngine {
    fn measure_run(&self, text: &str, style: &RichTextStyle) -> TextRunMetrics {
        let width = text.chars().count() as f64 * style.height * 0.6;
        TextRunMetrics {
            width,
            ascent: style.height * 0.8,
            descent: style.height * 0.2,
        }
    }
}

