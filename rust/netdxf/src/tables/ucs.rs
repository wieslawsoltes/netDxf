use std::collections::HashMap;

use crate::dxf_object_code::DxfObjectCode;
use crate::error::Result;
use crate::matrix3::Matrix3;
use crate::tables::table_object::{TableObject, TableObjectData};
use crate::tables::UcsOrthographicType;
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
    pub flags: i16,
    pub orthographic_view_type: Option<UcsOrthographicType>,
    pub base_ucs: Option<String>,
    pub orthographic_origins: HashMap<UcsOrthographicType, Vector3>,
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
            flags: 0,
            orthographic_view_type: None,
            base_ucs: None,
            orthographic_origins: HashMap::new(),
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

    /// Creates a `UCS` from an origin and a normal (Z-axis) vector using the Arbitrary Axis Algorithm.
    pub fn from_normal(
        name: impl Into<String>,
        origin: Vector3,
        normal: Vector3,
        rotation_rad: f64,
    ) -> Result<Self> {
        let z = Vector3::normalize(normal);
        let mut m = crate::math_helper::MathHelper::arbitrary_axis(z);
        if rotation_rad.abs() > f64::EPSILON {
            m = m * Matrix3::rotation_z(rotation_rad);
        }
        let x = Vector3::new(m.m11, m.m21, m.m31);
        let y = Vector3::new(m.m12, m.m22, m.m32);
        Self::from_origin_axes(name, origin, x, y)
    }

    /// Creates a `UCS` from an origin, a point on the positive X-axis, and a point in the XY plane.
    pub fn from_x_axis_and_point_on_xy_plane(
        name: impl Into<String>,
        origin: Vector3,
        point_on_x_axis: Vector3,
        point_on_xy_plane: Vector3,
    ) -> Result<Self> {
        let x_dir = point_on_x_axis - origin;
        let xy_dir = point_on_xy_plane - origin;
        Self::from_origin_axes(name, origin, x_dir, xy_dir)
    }

    /// Returns the 3x3 rotation matrix transforming UCS coordinates to WCS coordinates.
    pub fn get_transformation(&self) -> Matrix3 {
        Matrix3::new(
            self.x_axis.x, self.y_axis.x, self.z_axis.x,
            self.x_axis.y, self.y_axis.y, self.z_axis.y,
            self.x_axis.z, self.y_axis.z, self.z_axis.z,
        )
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
