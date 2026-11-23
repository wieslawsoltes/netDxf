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

/// MText rich-text parser supporting `\P`, `\L`/`\l`, `\O`/`\o`, `\K`/`\k`, `\H...;`, `\C...;`, and `{...}` groups.
pub struct RichText;

impl RichText {
    pub fn parse(
        input: &str,
        base: RichTextStyle,
        options: &RichTextParseOptions,
    ) -> RichTextDocument {
        let mut diagnostics = Vec::new();
        if input.len() > options.maximum_length {
            diagnostics.push(TextFormatDiagnostic {
                offset: options.maximum_length,
                message: "Input exceeds maximum rich text length.".into(),
            });
            return RichTextDocument {
                inlines: Vec::new(),
                paragraph_style: RichTextParagraphStyle::default(),
                diagnostics,
            };
        }

        let mut inlines = Vec::new();
        let mut style_stack = vec![base.clone()];
        let mut cur_style = base;
        let mut buf = String::new();

        let flush = |buf: &mut String, style: &RichTextStyle, out: &mut Vec<RichTextInline>| {
            if !buf.is_empty() {
                out.push(RichTextInline {
                    text: std::mem::take(buf),
                    style: style.clone(),
                });
            }
        };

        let chars: Vec<char> = input.chars().collect();
        let mut i = 0usize;
        while i < chars.len() {
            match chars[i] {
                '{' => {
                    flush(&mut buf, &cur_style, &mut inlines);
                    if style_stack.len() < options.maximum_depth {
                        style_stack.push(cur_style.clone());
                    }
                    i += 1;
                }
                '}' => {
                    flush(&mut buf, &cur_style, &mut inlines);
                    if style_stack.len() > 1 {
                        cur_style = style_stack.pop().unwrap_or(cur_style);
                    }
                    i += 1;
                }
                '\\' if i + 1 < chars.len() => {
                    let cmd = chars[i + 1];
                    match cmd {
                        'P' => {
                            buf.push('\n');
                            i += 2;
                        }
                        '~' => {
                            buf.push(' ');
                            i += 2;
                        }
                        '\\' | '{' | '}' => {
                            buf.push(cmd);
                            i += 2;
                        }
                        'L' => {
                            flush(&mut buf, &cur_style, &mut inlines);
                            cur_style.decoration = TextDecoration::Underline;
                            i += 2;
                        }
                        'l' | 'o' | 'k' => {
                            flush(&mut buf, &cur_style, &mut inlines);
                            cur_style.decoration = TextDecoration::None;
                            i += 2;
                        }
                        'O' => {
                            flush(&mut buf, &cur_style, &mut inlines);
                            cur_style.decoration = TextDecoration::Overline;
                            i += 2;
                        }
                        'K' => {
                            flush(&mut buf, &cur_style, &mut inlines);
                            cur_style.decoration = TextDecoration::StrikeThrough;
                            i += 2;
                        }
                        'H' | 'C' | 'f' | 'F' => {
                            flush(&mut buf, &cur_style, &mut inlines);
                            let start = i + 2;
                            let mut end = start;
                            while end < chars.len() && chars[end] != ';' {
                                end += 1;
                            }
                            let arg: String = chars[start..end].iter().collect();
                            match cmd {
                                'H' => {
                                    if let Some(mult) = arg.strip_suffix('x') {
                                        if let Ok(m) = mult.parse::<f64>() {
                                            if m > 0.0 {
                                                cur_style.height *= m;
                                            }
                                        }
                                    } else if let Ok(h) = arg.parse::<f64>() {
                                        if h > 0.0 {
                                            cur_style.height = h;
                                        }
                                    }
                                }
                                'C' => {
                                    if let Ok(aci) = arg.parse::<u32>() {
                                        cur_style.argb = Some(0xFF00_0000 | (aci * 0x01_0101));
                                    }
                                }
                                'f' | 'F' => {
                                    let parts: Vec<&str> = arg.split('|').collect();
                                    if let Some(&font) = parts.first() {
                                        if !font.is_empty() {
                                            cur_style.font = font.to_string();
                                        }
                                    }
                                    for part in parts.iter().skip(1) {
                                        if *part == "b1" {
                                            cur_style.bold = true;
                                        } else if *part == "b0" {
                                            cur_style.bold = false;
                                        } else if *part == "i1" {
                                            cur_style.italic = true;
                                        } else if *part == "i0" {
                                            cur_style.italic = false;
                                        }
                                    }
                                }
                                _ => {}
                            }
                            i = if end < chars.len() { end + 1 } else { end };
                        }
                        _ => {
                            buf.push('\\');
                            buf.push(cmd);
                            i += 2;
                        }
                    }
                }
                c => {
                    buf.push(c);
                    i += 1;
                }
            }
        }
        flush(&mut buf, &cur_style, &mut inlines);

        RichTextDocument {
            inlines,
            paragraph_style: RichTextParagraphStyle::default(),
            diagnostics,
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
                    style: inline.style.clone(),
                    metrics,
                });
                cur_x += metrics.width;
                line.width = cur_x;
                line.height = line.height.max(metrics.ascent + metrics.descent);
            }
        }

        let total_width = lines.iter().map(|l| l.width).fold(0.0_f64, f64::max);
        let total_height = lines.iter().map(|l| l.height).sum();
        RichTextLayoutResult {
            lines,
            total_width,
            total_height,
        }
    }
}

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
