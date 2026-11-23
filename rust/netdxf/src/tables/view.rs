use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::vector3::Vector3;

/// Represents a named view (`VIEW`).
#[derive(Clone, Debug, PartialEq)]
pub struct View {
    pub table: TableObjectData,
    pub target: Vector3,
    pub camera: Vector3,
    pub height: f64,
    pub width: f64,
    pub rotation: f64,
    pub front_clipping_plane: f64,
    pub back_clipping_plane: f64,
    pub fov: f64,
}

impl View {
    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::VIEW, true)?,
            target: Vector3::ZERO,
            camera: Vector3::UNIT_Z,
            height: 1.0,
            width: 1.0,
            rotation: 0.0,
            front_clipping_plane: 0.0,
            back_clipping_plane: 0.0,
            fov: 40.0,
        })
    }
}

impl TableObject for View {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
