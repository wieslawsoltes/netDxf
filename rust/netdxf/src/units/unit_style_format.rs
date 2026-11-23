use crate::error::{DxfError, Result};
use crate::units::FractionFormatType;

/// Represents the parameters to convert linear and angular units to their string representation.
#[derive(Debug, Clone, PartialEq)]
pub struct UnitStyleFormat {
    pub linear_decimal_places: i16,
    pub angular_decimal_places: i16,
    pub decimal_separator: String,
    pub feet_inches_separator: String,
    pub degrees_symbol: String,
    pub minutes_symbol: String,
    pub seconds_symbol: String,
    pub radians_symbol: String,
    pub gradians_symbol: String,
    pub feet_symbol: String,
    pub inches_symbol: String,
    pub fraction_height_scale: f64,
    pub fraction_type: FractionFormatType,
    pub suppress_linear_leading_zeros: bool,
    pub suppress_linear_trailing_zeros: bool,
    pub suppress_angular_leading_zeros: bool,
    pub suppress_angular_trailing_zeros: bool,
    pub suppress_zero_feet: bool,
    pub suppress_zero_inches: bool,
}

impl Default for UnitStyleFormat {
    fn default() -> Self {
        Self::new()
    }
}

impl UnitStyleFormat {
    pub fn new() -> Self {
        Self {
            linear_decimal_places: 2,
            angular_decimal_places: 0,
            decimal_separator: ".".to_string(),
            feet_inches_separator: "-".to_string(),
            degrees_symbol: "°".to_string(),
            minutes_symbol: "'".to_string(),
            seconds_symbol: "\"".to_string(),
            radians_symbol: "r".to_string(),
            gradians_symbol: "g".to_string(),
            feet_symbol: "'".to_string(),
            inches_symbol: "\"".to_string(),
            fraction_height_scale: 1.0,
            fraction_type: FractionFormatType::Horizontal,
            suppress_linear_leading_zeros: false,
            suppress_linear_trailing_zeros: false,
            suppress_angular_leading_zeros: false,
            suppress_angular_trailing_zeros: false,
            suppress_zero_feet: true,
            suppress_zero_inches: true,
        }
    }

    pub fn set_linear_decimal_places(&mut self, value: i16) -> Result<()> {
        if value < 0 {
            return Err(DxfError::InvalidValue(
                "The number of decimal places must be equal to or greater than zero.".into(),
            ));
        }
        self.linear_decimal_places = value;
        Ok(())
    }

    pub fn set_angular_decimal_places(&mut self, value: i16) -> Result<()> {
        if value < 0 {
            return Err(DxfError::InvalidValue(
                "The number of decimal places must be equal to or greater than zero.".into(),
            ));
        }
        self.angular_decimal_places = value;
        Ok(())
    }

    pub fn set_fraction_height_scale(&mut self, value: f64) -> Result<()> {
        if !value.is_finite() || value <= 0.0 {
            return Err(DxfError::InvalidValue(
                "The fraction height scale must be greater than zero.".into(),
            ));
        }
        self.fraction_height_scale = value;
        Ok(())
    }
}
