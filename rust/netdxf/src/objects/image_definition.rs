use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::{TableObject, TableObjectData};
use crate::units::ImageResolutionUnits;

/// Represents a DXF `IMAGEDEF` object.
#[derive(Clone, Debug, PartialEq)]
pub struct ImageDefinition {
    pub table: TableObjectData,
    pub file: String,
    pub width: i32,
    pub height: i32,
    pub horizontal_resolution: f64,
    pub vertical_resolution: f64,
    pub resolution_units: ImageResolutionUnits,
}

impl ImageDefinition {
    pub fn new(
        name: impl Into<String>,
        file: impl Into<String>,
        width: i32,
        height: i32,
    ) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::IMAGE_DEF, true)?,
            file: file.into(),
            width,
            height,
            horizontal_resolution: 72.0,
            vertical_resolution: 72.0,
            resolution_units: ImageResolutionUnits::Inches,
        })
    }
}

impl TableObject for ImageDefinition {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
