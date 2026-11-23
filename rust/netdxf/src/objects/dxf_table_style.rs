use crate::aci_color::AciColor;
use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::{TableObject, TableObjectData, TextStyle};

/// Represents a DXF `TABLESTYLE` object (`netDxf.Objects.DxfTableStyle`).
#[derive(Clone, Debug, PartialEq)]
pub struct DxfTableStyle {
    pub table: TableObjectData,
    pub description: String,
    pub flow_direction: i16,
    pub horizontal_cell_margin: f64,
    pub vertical_cell_margin: f64,
    pub suppress_title: bool,
    pub suppress_header: bool,
    pub text_style: String,
    pub text_height: f64,
    pub text_color: AciColor,
    pub grid_color: AciColor,
}

impl DxfTableStyle {
    pub const DEFAULT_NAME: &'static str = "Standard";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        let mut s = Self::default_style();
        s.table = TableObjectData::new(name, DxfObjectCode::DICTIONARY, true)?;
        s.table.object.code_name = "TABLESTYLE".to_string();
        Ok(s)
    }

    pub fn default_style() -> Self {
        let mut table = TableObjectData::reserved(Self::DEFAULT_NAME, "TABLESTYLE");
        table.object.code_name = "TABLESTYLE".to_string();
        Self {
            table,
            description: String::new(),
            flow_direction: 0,
            horizontal_cell_margin: 0.06,
            vertical_cell_margin: 0.06,
            suppress_title: false,
            suppress_header: false,
            text_style: TextStyle::DEFAULT_NAME.to_string(),
            text_height: 0.18,
            text_color: AciColor::BY_BLOCK,
            grid_color: AciColor::BY_BLOCK,
        }
    }
}

impl TableObject for DxfTableStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
