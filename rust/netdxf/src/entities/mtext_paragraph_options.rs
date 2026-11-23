use crate::entities::{
    MTextLineSpacingStyle, MTextParagraphAlignment, MTextParagraphVerticalAlignment,
};

/// Options for `MText` paragraph formatting.
#[derive(Debug, Clone, PartialEq)]
pub struct MTextParagraphOptions {
    pub height_factor: f64,
    pub alignment: MTextParagraphAlignment,
    pub vertical_alignment: MTextParagraphVerticalAlignment,
    pub spacing_before: f64,
    pub spacing_after: f64,
    pub first_line_indent: f64,
    pub left_indent: f64,
    pub right_indent: f64,
    pub line_spacing_factor: f64,
    pub line_spacing_style: MTextLineSpacingStyle,
}

impl Default for MTextParagraphOptions {
    fn default() -> Self {
        Self {
            height_factor: 1.0,
            alignment: MTextParagraphAlignment::Left,
            vertical_alignment: MTextParagraphVerticalAlignment::Center,
            spacing_before: 0.0,
            spacing_after: 0.0,
            first_line_indent: 0.0,
            left_indent: 0.0,
            right_indent: 0.0,
            line_spacing_factor: 1.0,
            line_spacing_style: MTextLineSpacingStyle::AtLeast,
        }
    }
}
