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

    #[inline]
    pub fn properties(&self) -> crate::entities::mleader_context::MLeaderStyleProperties {
        crate::entities::mleader_context::MLeaderStyleProperties {
            content_type: self.content_type,
            leader_line_type: self.leader_line_type,
            leader_line_color: self.leader_line_color,
            leader_line_type_name: self.leader_linetype.clone(),
            enable_landing: self.enable_landing,
            landing_gap: self.landing_gap,
            enable_dogleg: self.enable_dogleg,
            dogleg_length: self.dogleg_length,
            arrow_size: self.arrow_size,
            text_style: self.text_style.clone(),
            text_height: self.text_height,
            text_color: self.text_color,
            scale: self.scale,
            ..Default::default()
        }
    }

    #[inline]
    pub fn stored_envelope_value(&self) -> i16 {
        2
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
