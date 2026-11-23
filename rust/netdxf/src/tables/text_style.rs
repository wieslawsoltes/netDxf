use bitflags::bitflags;

use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::font_style::FontStyle;
use crate::tables::table_object::{TableObject, TableObjectData};

bitflags! {
    /// Text style flags (group code 70).
    #[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
    pub struct TextStyleFlags: i16 {
        const NONE = 0;
        const SHAPE = 1;
        const VERTICAL = 4;
    }
}

/// Represents a DXF text style (`STYLE`).
#[derive(Clone, Debug, PartialEq)]
pub struct TextStyle {
    pub table: TableObjectData,
    pub font_file: String,
    pub big_font: String,
    pub font_family_name: String,
    pub font_style: FontStyle,
    pub height: f64,
    pub is_backward: bool,
    pub is_upside_down: bool,
    pub is_vertical: bool,
    pub oblique_angle: f64,
    pub width_factor: f64,
}

impl TextStyle {
    pub const DEFAULT_NAME: &'static str = "Standard";
    pub const DEFAULT_FONT: &'static str = "simplex.shx";

    pub fn new(name: impl Into<String>, font_file: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::TEXT_STYLE, true)?,
            font_file: font_file.into(),
            big_font: String::new(),
            font_family_name: String::new(),
            font_style: FontStyle::REGULAR,
            height: 0.0,
            is_backward: false,
            is_upside_down: false,
            is_vertical: false,
            oblique_angle: 0.0,
            width_factor: 1.0,
        })
    }

    pub fn default_style() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::TEXT_STYLE),
            font_file: Self::DEFAULT_FONT.to_string(),
            big_font: String::new(),
            font_family_name: String::new(),
            font_style: FontStyle::REGULAR,
            height: 0.0,
            is_backward: false,
            is_upside_down: false,
            is_vertical: false,
            oblique_angle: 0.0,
            width_factor: 1.0,
        }
    }
}

impl TableObject for TextStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
