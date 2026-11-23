use crate::error::{DxfError, Result};
use crate::math_helper::MathHelper;
use crate::units::unit_format_math::{self, BigUint};
use crate::units::{AngleUnitType, UnitStyleFormat};

/// Formats finite angles supplied in degrees using explicit unit settings.
pub struct AngleUnitFormat;

impl AngleUnitFormat {
    /// Formats an angle in degrees using any declared angular unit type.
    pub fn format(
        mut angle: f64,
        units: AngleUnitType,
        format: &UnitStyleFormat,
        normalize: bool,
    ) -> Result<String> {
        unit_format_math::validate(angle, format, true)?;
        if normalize {
            angle = Self::normalize(angle);
        }
        match units {
            AngleUnitType::DecimalDegrees => Self::to_decimal(angle, format),
            AngleUnitType::DegreesMinutesSeconds => Self::to_degrees_minutes_seconds(angle, format),
            AngleUnitType::Gradians => Self::to_gradians(angle, format),
            AngleUnitType::Radians => Self::to_radians(angle, format),
            AngleUnitType::SurveyorUnits => Self::to_surveyor(angle, format, true),
        }
    }

    /// Converts an angle in degrees to decimal degrees with the configured symbol.
    pub fn to_decimal(angle: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(angle, format, true)?;
        let body = unit_format_math::fixed(
            angle,
            places,
            &format.decimal_separator,
            format.suppress_angular_leading_zeros,
            format.suppress_angular_trailing_zeros,
        )?;
        Ok(format!("{body}{}", format.degrees_symbol))
    }

    /// Converts an angle in degrees to degrees, minutes, and seconds.
    pub fn to_degrees_minutes_seconds(angle: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(angle, format, true)?;
        let decimals = places.saturating_sub(4);
        let scale = 10u64.pow(decimals as u32);
        let per_degree = if places == 0 {
            1
        } else if places <= 2 {
            60
        } else {
            3600 * scale
        };
        let rounded = unit_format_math::round_magnitude(angle, per_degree);
        let (degrees, remainder) = rounded.div_rem_u64(per_degree);
        let mut text = format!("{}{}", degrees.to_decimal_string(), format.degrees_symbol);
        if places > 0 {
            let (minutes, rem) = if places <= 2 {
                (remainder, 0)
            } else {
                (remainder / (60 * scale), remainder % (60 * scale))
            };
            text.push_str(&format!("{minutes}{}", format.minutes_symbol));
            if places > 2 {
                let sec = unit_format_math::decimal(
                    &BigUint::from_u64(rem),
                    decimals,
                    &format.decimal_separator,
                    false,
                    false,
                );
                text.push_str(&format!("{sec}{}", format.seconds_symbol));
            }
        }
        let sign = if angle < 0.0 && !rounded.is_zero() {
            "-"
        } else {
            ""
        };
        Ok(format!("{sign}{text}"))
    }

    /// Converts an angle in degrees to gradians.
    pub fn to_gradians(angle: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(angle, format, true)?;
        let converted = angle * MathHelper::DEG_TO_GRAD;
        if !converted.is_finite() {
            return Err(DxfError::InvalidValue(
                "The gradian angle is outside binary64 range.".into(),
            ));
        }
        let body = unit_format_math::fixed(
            converted,
            places,
            &format.decimal_separator,
            format.suppress_angular_leading_zeros,
            format.suppress_angular_trailing_zeros,
        )?;
        Ok(format!("{body}{}", format.gradians_symbol))
    }

    /// Converts an angle in degrees to radians.
    pub fn to_radians(angle: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(angle, format, true)?;
        let body = unit_format_math::fixed(
            angle * MathHelper::DEG_TO_RAD,
            places,
            &format.decimal_separator,
            format.suppress_angular_leading_zeros,
            format.suppress_angular_trailing_zeros,
        )?;
        Ok(format!("{body}{}", format.radians_symbol))
    }

    /// Converts a counterclockwise angle from east to a quadrant surveyor bearing.
    pub fn to_surveyor(
        angle: f64,
        format: &UnitStyleFormat,
        include_spaces: bool,
    ) -> Result<String> {
        unit_format_math::validate(angle, format, true)?;
        let normalized = Self::normalize(angle);
        let (ns, ew, bearing) = if normalized <= 90.0 {
            ("N", "E", 90.0 - normalized)
        } else if normalized <= 180.0 {
            ("N", "W", normalized - 90.0)
        } else if normalized <= 270.0 {
            ("S", "W", 270.0 - normalized)
        } else {
            ("S", "E", normalized - 270.0)
        };
        let gap = if include_spaces { " " } else { "" };
        let dms = Self::to_degrees_minutes_seconds(bearing, format)?;
        Ok(format!("{ns}{gap}{dms}{gap}{ew}"))
    }

    fn normalize(mut angle: f64) -> f64 {
        angle %= 360.0;
        if angle < 0.0 {
            angle += 360.0;
        }
        if angle == 0.0 || angle == 360.0 {
            0.0
        } else {
            angle
        }
    }
}
