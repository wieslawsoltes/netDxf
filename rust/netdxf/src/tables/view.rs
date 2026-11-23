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
    pub lens_length: f64,
    pub flags: i16,
    pub is_paper_space: bool,
    pub is_camera_plottable: bool,
    pub live_section: Option<String>,
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
            lens_length: 50.0,
            flags: 0,
            is_paper_space: false,
            is_camera_plottable: false,
            live_section: None,
        })
    }

    #[inline]
    pub fn has_stored_live_section(&self) -> bool {
        self.live_section.as_deref().is_some_and(|s| !s.is_empty())
    }

    pub fn clear_live_section_reference(&mut self) {
        self.live_section = None;
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
