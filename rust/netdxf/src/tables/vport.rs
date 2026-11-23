use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a model space viewport (`VPORT`).
#[derive(Clone, Debug, PartialEq)]
pub struct VPort {
    pub table: TableObjectData,
    pub view_center: Vector2,
    pub snap_base_point: Vector2,
    pub snap_spacing: Vector2,
    pub grid_spacing: Vector2,
    pub target: Vector3,
    pub direction: Vector3,
    pub view_height: f64,
    pub view_aspect_ratio: f64,
    pub show_grid: bool,
    pub snap_mode: bool,
}

impl VPort {
    pub const DEFAULT_NAME: &'static str = "*Active";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::VPORT, true)?,
            view_center: Vector2::ZERO,
            snap_base_point: Vector2::ZERO,
            snap_spacing: Vector2::new(0.5, 0.5),
            grid_spacing: Vector2::new(10.0, 10.0),
            target: Vector3::ZERO,
            direction: Vector3::UNIT_Z,
            view_height: 10.0,
            view_aspect_ratio: 1.0,
            show_grid: true,
            snap_mode: false,
        })
    }

    pub fn active() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::VPORT),
            view_center: Vector2::ZERO,
            snap_base_point: Vector2::ZERO,
            snap_spacing: Vector2::new(0.5, 0.5),
            grid_spacing: Vector2::new(10.0, 10.0),
            target: Vector3::ZERO,
            direction: Vector3::UNIT_Z,
            view_height: 10.0,
            view_aspect_ratio: 1.0,
            show_grid: true,
            snap_mode: false,
        }
    }
}

impl TableObject for VPort {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
