use netdxf_shadow::Vec3;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default, Serialize, Deserialize)]
pub enum TextDecoration {
    #[default]
    None,
    Underline,
    Overline,
    StrikeThrough,
}

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
    pub tab_stops: Vec<ParagraphTabStop>,
}

pub struct MTextParagraphControl;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RichTextStyle {
    pub font: String,
    pub height: f64,
    pub bold: bool,
    pub italic: bool,
    pub argb: Option<u32>,
    pub decoration: TextDecoration,
}

impl Default for RichTextStyle {
    fn default() -> Self {
        Self {
            font: "Arial".into(),
            height: 2.5,
            bold: false,
            italic: false,
            argb: None,
            decoration: TextDecoration::None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RichTextInline {
    pub text: String,
    pub style: RichTextStyle,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct TextFormatDiagnostic {
    pub offset: usize,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextDocument {
    pub inlines: Vec<RichTextInline>,
    pub paragraph_style: RichTextParagraphStyle,
    pub diagnostics: Vec<TextFormatDiagnostic>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub struct RichTextParseOptions {
    pub maximum_length: usize,
    pub maximum_depth: usize,
}

impl Default for RichTextParseOptions {
    fn default() -> Self {
        Self {
            maximum_length: 1_048_576,
            maximum_depth: 64,
        }
    }
}

/// MText rich-text parser.
pub struct RichText;

impl RichText {
    pub fn parse(input: &str, base: RichTextStyle, _options: &RichTextParseOptions) -> RichTextDocument {
        let plain = input.replace("\\P", "\n");
        RichTextDocument {
            inlines: vec![RichTextInline {
                text: plain,
                style: base,
            }],
            paragraph_style: RichTextParagraphStyle::default(),
            diagnostics: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct TextRunMetrics {
    pub width: f64,
    pub ascent: f64,
    pub descent: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PositionedTextRun {
    pub text: String,
    pub origin: Vec3,
    pub style: RichTextStyle,
    pub metrics: TextRunMetrics,
}

#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct TextRule {
    pub start: Vec3,
    pub end: Vec3,
    pub thickness: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextLine {
    pub runs: Vec<PositionedTextRun>,
    pub rules: Vec<TextRule>,
    pub width: f64,
    pub height: f64,
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextLayoutResult {
    pub lines: Vec<RichTextLine>,
    pub total_width: f64,
    pub total_height: f64,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct TextLine {
    pub text: String,
    pub width: f64,
}

pub struct TextLayout;
pub struct RichTextLayout;

pub trait ITextLayoutEngine: Send + Sync {
    fn measure_run(&self, text: &str, style: &RichTextStyle) -> TextRunMetrics;
}

#[derive(Debug, Default)]
pub struct ApproximateTextLayoutEngine;

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
