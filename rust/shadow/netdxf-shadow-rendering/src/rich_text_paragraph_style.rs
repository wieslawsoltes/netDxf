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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ParagraphAlignment {
    #[default]
    Left,
    Center,
    Right,
    Justified,
    Distributed,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct ParagraphLineSpacing {
    pub factor: f64,
    pub exact: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum ParagraphTabAlignment {
    #[default]
    Left,
    Center,
    Right,
    Decimal,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct ParagraphTabStop {
    pub position: f64,
    pub alignment: ParagraphTabAlignment,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextParagraphStyle {
    pub alignment: ParagraphAlignment,
    pub first_line_indent: f64,
    pub left_indent: f64,
    pub right_indent: f64,
    pub space_before: f64,
    pub space_after: f64,
    pub line_spacing: Option<ParagraphLineSpacing>,
    pub line_spacing_value: f64,
    pub tab_stops: Vec<ParagraphTabStop>,
}

impl RichTextParagraphStyle {
    pub fn tabs(&self) -> &[ParagraphTabStop] {
        &self.tab_stops
    }

    pub fn validate(&self) -> Result<(), String> {
        if !self.first_line_indent.is_finite()
            || !self.left_indent.is_finite()
            || !self.right_indent.is_finite()
            || !self.space_before.is_finite()
            || !self.space_after.is_finite()
        {
            return Err("Paragraph metrics must be finite.".into());
        }
        if let Some(ls) = self.line_spacing {
            if !ls.factor.is_finite() || ls.factor <= 0.0 {
                return Err("Paragraph line spacing factor must be positive and finite.".into());
            }
        }
        Ok(())
    }
}

pub struct MTextParagraphControl;

impl MTextParagraphControl {
    /// Parses an MText `\px...;` paragraph control directive into [`RichTextParagraphStyle`].
    pub fn parse_paragraph_code(code: &str) -> RichTextParagraphStyle {
        let mut style = RichTextParagraphStyle::default();
        let body = code.trim_start_matches("\\px").trim_end_matches(';');
        for token in body.split(',') {
            if let Some(rest) = token.strip_prefix('q') {
                style.alignment = match rest {
                    "c" => ParagraphAlignment::Center,
                    "r" => ParagraphAlignment::Right,
                    "j" => ParagraphAlignment::Justified,
                    "d" => ParagraphAlignment::Distributed,
                    _ => ParagraphAlignment::Left,
                };
            } else if let Some(rest) = token.strip_prefix('i') {
                if let Ok(v) = rest.parse::<f64>() {
                    style.first_line_indent = v;
                }
            } else if let Some(rest) = token.strip_prefix('l') {
                if let Ok(v) = rest.parse::<f64>() {
                    style.left_indent = v;
                }
            } else if let Some(rest) = token.strip_prefix('r') {
                if let Ok(v) = rest.parse::<f64>() {
                    style.right_indent = v;
                }
            }
        }
        style
    }
}

