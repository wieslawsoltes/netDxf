use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::vector3::Vector3;

/// Represents a User Coordinate System (`UCS`).
#[derive(Clone, Debug, PartialEq)]
pub struct UCS {
    pub table: TableObjectData,
    pub origin: Vector3,
    pub x_axis: Vector3,
    pub y_axis: Vector3,
    pub z_axis: Vector3,
    pub elevation: f64,
}

impl UCS {
    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::UCS, true)?,
            origin: Vector3::ZERO,
            x_axis: Vector3::UNIT_X,
            y_axis: Vector3::UNIT_Y,
            z_axis: Vector3::UNIT_Z,
            elevation: 0.0,
        })
    }

    pub fn from_origin_axes(
        name: impl Into<String>,
        origin: Vector3,
        x_direction: Vector3,
        y_direction: Vector3,
    ) -> Result<Self> {
        let mut ucs = Self::new(name)?;
        ucs.origin = origin;
        let x = Vector3::normalize(x_direction);
        let z = Vector3::normalize(Vector3::cross_product(x, y_direction));
        let y = Vector3::normalize(Vector3::cross_product(z, x));
        ucs.x_axis = x;
        ucs.y_axis = y;
        ucs.z_axis = z;
        Ok(ucs)
    }
}

impl TableObject for UCS {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
