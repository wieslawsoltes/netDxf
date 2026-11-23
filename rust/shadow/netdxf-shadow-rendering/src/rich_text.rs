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
pub enum TextDecoration {
    #[default]
    None,
    Underline,
    Overline,
    StrikeThrough,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RichTextStyle {
    pub font: String,
    pub font_family: String,
    pub height: f64,
    pub width_factor: f64,
    pub oblique_degrees: f64,
    pub tracking: f64,
    pub bold: bool,
    pub italic: bool,
    pub color: Option<u32>,
    pub argb: Option<u32>,
    pub vertical_alignment: String,
    pub paragraph: Option<RichTextParagraphStyle>,
    pub decoration: TextDecoration,
}

impl Default for RichTextStyle {
    fn default() -> Self {
        Self {
            font: "Arial".into(),
            font_family: "Arial".into(),
            height: 2.5,
            width_factor: 1.0,
            oblique_degrees: 0.0,
            tracking: 1.0,
            bold: false,
            italic: false,
            color: None,
            argb: None,
            vertical_alignment: "Bottom".into(),
            paragraph: None,
            decoration: TextDecoration::None,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Default, Serialize, Deserialize)]
pub struct RichTextInline {
    pub text: String,
    pub lower: Option<String>,
    pub stack_separator: Option<char>,
    pub is_paragraph_control: bool,
    pub source_start: usize,
    pub source_length: usize,
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
                let len = buf.len();
                out.push(RichTextInline {
                    text: std::mem::take(buf),
                    source_length: len,
                    style: style.clone(),
                    ..Default::default()
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

