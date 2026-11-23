use crate::error::{DxfError, Result};
use crate::tables::FontStyle;

/// Immutable stored STYLE extended font family and complete group 1071 value.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TextStyleFontData {
    pub family_name: String,
    pub flags: i32,
}

impl TextStyleFontData {
    pub fn new(family_name: impl Into<String>, flags: i32) -> Result<Self> {
        let family_name = family_name.into();
        if family_name.len() > 255 {
            return Err(DxfError::InvalidValue(
                "The font family XData string cannot exceed 255 characters.".into(),
            ));
        }
        Ok(Self { family_name, flags })
    }

    /// Gets the bold and italic bits extracted from group 1071.
    pub fn font_style(&self) -> FontStyle {
        FontStyle::from_bits_truncate((self.flags >> 24) & 3)
    }
}
