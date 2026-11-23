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
        const EXTERNALLY_DEPENDENT = 16;
        const EXTERNAL_REFERENCE_RESOLVED = 32;
        const REFERENCED = 64;
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
    pub last_height: f64,
    pub is_backward: bool,
    pub is_upside_down: bool,
    pub is_vertical: bool,
    pub oblique_angle: f64,
    pub width_factor: f64,
    pub flags: TextStyleFlags,
    pub text_generation_flags: i16,
    pub extended_font_data: Option<String>,
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
            last_height: 0.2,
            is_backward: false,
            is_upside_down: false,
            is_vertical: false,
            oblique_angle: 0.0,
            width_factor: 1.0,
            flags: TextStyleFlags::NONE,
            text_generation_flags: 0,
            extended_font_data: None,
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
            last_height: 0.2,
            is_backward: false,
            is_upside_down: false,
            is_vertical: false,
            oblique_angle: 0.0,
            width_factor: 1.0,
            flags: TextStyleFlags::NONE,
            text_generation_flags: 0,
            extended_font_data: None,
        }
    }

    /// Creates a `TextStyle` preserving raw stored primary and big font file names.
    pub fn from_stored_font_files(
        name: impl Into<String>,
        font_file: impl Into<String>,
        big_font: impl Into<String>,
    ) -> Result<Self> {
        let mut style = Self::new(name, font_file)?;
        style.big_font = big_font.into();
        Ok(style)
    }

    /// Updates raw stored primary and big font file names.
    pub fn set_stored_font_files(
        &mut self,
        font_file: impl Into<String>,
        big_font: impl Into<String>,
    ) {
        self.font_file = font_file.into();
        self.big_font = big_font.into();
    }
}

impl Default for TextStyle {
    fn default() -> Self {
        Self::default_style()
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
