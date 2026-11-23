use crate::aci_color::AciColor;

/// Options for `MText` inline text formatting.
#[derive(Debug, Clone, PartialEq)]
pub struct MTextFormattingOptions {
    pub bold: bool,
    pub italic: bool,
    pub overline: bool,
    pub underline: bool,
    pub strike_through: bool,
    pub superscript: bool,
    pub subscript: bool,
    pub color: Option<AciColor>,
    pub font_name: Option<String>,
    pub height_factor: f64,
    pub oblique_angle: f64,
    pub character_space_factor: f64,
    pub width_factor: f64,
    pub super_sub_script_height_factor: f64,
}

impl Default for MTextFormattingOptions {
    fn default() -> Self {
        Self {
            bold: false,
            italic: false,
            overline: false,
            underline: false,
            strike_through: false,
            superscript: false,
            subscript: false,
            color: None,
            font_name: None,
            height_factor: 1.0,
            oblique_angle: 0.0,
            character_space_factor: 1.0,
            width_factor: 1.0,
            super_sub_script_height_factor: 0.7,
        }
    }
}
