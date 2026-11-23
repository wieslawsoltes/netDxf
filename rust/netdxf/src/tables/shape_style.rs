use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};

/// Represents a DXF shape style (`STYLE` with shape flag bit 1).
#[derive(Clone, Debug, PartialEq)]
pub struct ShapeStyle {
    pub table: TableObjectData,
    pub file: String,
    pub size: f64,
    pub width_factor: f64,
    pub oblique_angle: f64,
}

impl ShapeStyle {
    pub fn new(name: impl Into<String>, file: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::TEXT_STYLE, false)?,
            file: file.into(),
            size: 0.0,
            width_factor: 1.0,
            oblique_angle: 0.0,
        })
    }
}

impl TableObject for ShapeStyle {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
