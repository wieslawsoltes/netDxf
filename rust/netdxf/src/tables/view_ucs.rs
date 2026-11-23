use crate::error::{DxfError, Result};
use crate::vector3::Vector3;

/// The stored UCS associated with a named VIEW, available in DXF 2000 and later.
#[derive(Debug, Clone, PartialEq)]
pub struct ViewUcs {
    pub origin: Vector3,
    pub x_axis: Vector3,
    pub y_axis: Vector3,
    pub orthographic_type: i16,
    pub elevation: f64,
    pub named_ucs: Option<String>,
    pub base_ucs: Option<String>,
}

impl Default for ViewUcs {
    fn default() -> Self {
        Self::new()
    }
}

impl ViewUcs {
    pub fn new() -> Self {
        Self {
            origin: Vector3::ZERO,
            x_axis: Vector3::UNIT_X,
            y_axis: Vector3::UNIT_Y,
            orthographic_type: 0,
            elevation: 0.0,
            named_ucs: None,
            base_ucs: None,
        }
    }

    pub fn validate(&self) -> Result<()> {
        if !self.origin.x.is_finite() || !self.origin.y.is_finite() || !self.origin.z.is_finite() {
            return Err(DxfError::InvalidValue(
                "A finite UCS origin is required.".into(),
            ));
        }
        if !self.x_axis.x.is_finite()
            || !self.x_axis.y.is_finite()
            || !self.x_axis.z.is_finite()
            || (self.x_axis.x == 0.0 && self.x_axis.y == 0.0 && self.x_axis.z == 0.0)
        {
            return Err(DxfError::InvalidValue(
                "A finite nonzero UCS X axis is required.".into(),
            ));
        }
        if !self.y_axis.x.is_finite()
            || !self.y_axis.y.is_finite()
            || !self.y_axis.z.is_finite()
            || (self.y_axis.x == 0.0 && self.y_axis.y == 0.0 && self.y_axis.z == 0.0)
        {
            return Err(DxfError::InvalidValue(
                "A finite nonzero UCS Y axis is required.".into(),
            ));
        }
        if !(0..=6).contains(&self.orthographic_type) {
            return Err(DxfError::InvalidValue(
                "The orthographic type must be between zero and six.".into(),
            ));
        }
        if !self.elevation.is_finite() {
            return Err(DxfError::InvalidValue(
                "A finite UCS elevation is required.".into(),
            ));
        }
        if self.base_ucs.is_some() && self.orthographic_type == 0 {
            return Err(DxfError::InvalidValue(
                "A VIEW base UCS requires a nonzero orthographic type.".into(),
            ));
        }
        Ok(())
    }
}
