use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::vector2::Vector2;
use crate::vector3::Vector3;

/// Represents a model space viewport (`VPORT`).
#[derive(Clone, Debug, PartialEq)]
pub struct VPort {
    pub table: TableObjectData,
    pub lower_left_corner: Vector2,
    pub upper_right_corner: Vector2,
    pub view_center: Vector2,
    pub snap_base_point: Vector2,
    pub snap_spacing: Vector2,
    pub grid_spacing: Vector2,
    pub target: Vector3,
    pub direction: Vector3,
    pub view_height: f64,
    pub view_aspect_ratio: f64,
    pub lens_length: f64,
    pub front_clipping_plane: f64,
    pub back_clipping_plane: f64,
    pub snap_rotation_angle: f64,
    pub view_twist_angle: f64,
    pub circle_sides: i16,
    pub fast_zoom: bool,
    pub show_grid: bool,
    pub snap_mode: bool,
    pub ucs_icon: i16,
    pub snap_isopair: i16,
    pub ucs_origin: Vector3,
    pub ucs_x_axis: Vector3,
    pub ucs_y_axis: Vector3,
    pub base_ucs: Option<String>,
    pub flags: i16,
}

impl VPort {
    pub const DEFAULT_NAME: &'static str = "*Active";

    pub fn new(name: impl Into<String>) -> Result<Self> {
        Ok(Self {
            table: TableObjectData::new(name, DxfObjectCode::VPORT, true)?,
            lower_left_corner: Vector2::ZERO,
            upper_right_corner: Vector2::new(1.0, 1.0),
            view_center: Vector2::ZERO,
            snap_base_point: Vector2::ZERO,
            snap_spacing: Vector2::new(0.5, 0.5),
            grid_spacing: Vector2::new(10.0, 10.0),
            target: Vector3::ZERO,
            direction: Vector3::UNIT_Z,
            view_height: 10.0,
            view_aspect_ratio: 1.0,
            lens_length: 50.0,
            front_clipping_plane: 0.0,
            back_clipping_plane: 0.0,
            snap_rotation_angle: 0.0,
            view_twist_angle: 0.0,
            circle_sides: 1000,
            fast_zoom: true,
            show_grid: true,
            snap_mode: false,
            ucs_icon: 3,
            snap_isopair: 0,
            ucs_origin: Vector3::ZERO,
            ucs_x_axis: Vector3::UNIT_X,
            ucs_y_axis: Vector3::UNIT_Y,
            base_ucs: None,
            flags: 0,
        })
    }

    pub fn active() -> Self {
        Self {
            table: TableObjectData::reserved(Self::DEFAULT_NAME, DxfObjectCode::VPORT),
            lower_left_corner: Vector2::ZERO,
            upper_right_corner: Vector2::new(1.0, 1.0),
            view_center: Vector2::ZERO,
            snap_base_point: Vector2::ZERO,
            snap_spacing: Vector2::new(0.5, 0.5),
            grid_spacing: Vector2::new(10.0, 10.0),
            target: Vector3::ZERO,
            direction: Vector3::UNIT_Z,
            view_height: 10.0,
            view_aspect_ratio: 1.0,
            lens_length: 50.0,
            front_clipping_plane: 0.0,
            back_clipping_plane: 0.0,
            snap_rotation_angle: 0.0,
            view_twist_angle: 0.0,
            circle_sides: 1000,
            fast_zoom: true,
            show_grid: true,
            snap_mode: false,
            ucs_icon: 3,
            snap_isopair: 0,
            ucs_origin: Vector3::ZERO,
            ucs_x_axis: Vector3::UNIT_X,
            ucs_y_axis: Vector3::UNIT_Y,
            base_ucs: None,
            flags: 0,
        }
    }

    #[inline] pub fn named_ucs(&self) -> Option<&str> { self.base_ucs.as_deref() }
    #[inline] pub fn render_mode(&self) -> i16 { 0 }
    #[inline] pub fn snap_rotation(&self) -> f64 { self.snap_rotation_angle }
    #[inline] pub fn snap_style(&self) -> i16 { self.snap_isopair }
    #[inline] pub fn sun(&self) -> Option<&str> { None }
    #[inline] pub fn ucs_elevation(&self) -> f64 { self.ucs_origin.z }
    #[inline] pub fn ucs_orthographic_type(&self) -> i16 { 0 }
    #[inline] pub fn view_direction(&self) -> Vector3 { self.direction }
    #[inline] pub fn view_target(&self) -> Vector3 { self.target }
    #[inline] pub fn view_twist(&self) -> f64 { self.view_twist_angle }
    #[inline] pub fn show_ucs_icon(&self) -> bool { (self.ucs_icon & 1) != 0 }
    #[inline] pub fn ucs_at_origin(&self) -> bool { (self.ucs_icon & 2) != 0 }
}

impl TableObject for VPort {
    fn table_data(&self) -> &TableObjectData {
        &self.table
    }
    fn table_data_mut(&mut self) -> &mut TableObjectData {
        &mut self.table
    }
}
