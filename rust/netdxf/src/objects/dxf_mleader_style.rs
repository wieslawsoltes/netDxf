use crate::aci_color::AciColor;
use crate::entities::multi_leader::MLeaderContentType;
use crate::error::Result;
use crate::tables::{Linetype, TableObject, TableObjectData, TextStyle};

/// Represents a DXF `MLEADERSTYLE` object (`netDxf.Objects.DxfMLeaderStyle`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfMLeaderStyle {
    pub table: TableObjectData,
    pub content_type: MLeaderContentType,
    pub leader_line_type: i16,
    pub leader_line_color: AciColor,
    pub leader_linetype: String,
    pub enable_landing: bool,
    pub landing_gap: f64,
    pub enable_dogleg: bool,
    pub dogleg_length: f64,
    pub arrow_size: f64,
    pub text_style: String,
    pub text_height: f64,
    pub text_color: AciColor,
    pub scale: f64,
}

impl DxfMLeaderStyle {
    pub const DEFAULT_NAME: &'static str = "Standard";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        let mut s = Self::default_style();
        s.table = TableObjectData::new(name, "MLEADERSTYLE", true)?;
        Ok(s)
    }

    pub fn default_style() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, "MLEADERSTYLE"),
            content_type: MLeaderContentType::MText,
            leader_line_type: 1,
            leader_line_color: AciColor::BY_BLOCK,
            leader_linetype: Linetype::BY_BLOCK_NAME.to_string(),
            enable_landing: true,
            landing_gap: 0.09,
            enable_dogleg: true,
            dogleg_length: 0.36,
            arrow_size: 0.18,
            text_style: TextStyle::DEFAULT_NAME.to_string(),
            text_height: 0.18,
            text_color: AciColor::BY_BLOCK,
            scale: 1.0,
        }
    }
}

impl TableObject for DxfMLeaderStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
