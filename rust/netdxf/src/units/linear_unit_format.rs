use crate::error::{DxfError, Result};
use crate::units::unit_format_math::{self, BigUint};
use crate::units::{FractionFormatType, UnitStyleFormat};

/// Formats finite lengths with explicit DXF precision and unit symbols.
pub struct LinearUnitFormat;

impl LinearUnitFormat {
    /// Formats a finite length using scientific notation.
    pub fn to_scientific(length: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(length, format, false)?;
        let formatted = format!("{length:.*E}", places);
        // Normalize exponent to E+00 / E-00 style matching C# "0.00E+00"
        let (mantissa, exp_part) = formatted
            .split_once('E')
            .unwrap_or((formatted.as_str(), "0"));
        let exp_val: i32 = exp_part.parse().unwrap_or(0);
        let exp_sign = if exp_val < 0 { '-' } else { '+' };
        let exp_abs = exp_val.unsigned_abs();
        let exp_str = format!("E{exp_sign}{exp_abs:02}");

        let (sign, unsigned_mantissa) = if let Some(rest) = mantissa.strip_prefix('-') {
            ("-", rest)
        } else {
            ("", mantissa)
        };
        let (mut int_part, mut frac_part) = unsigned_mantissa
            .split_once('.')
            .unwrap_or((unsigned_mantissa, ""));
        if format.suppress_linear_trailing_zeros {
            frac_part = frac_part.trim_end_matches('0');
        }
        if format.suppress_linear_leading_zeros && int_part == "0" && !frac_part.is_empty() {
            int_part = "";
        }
        let body = if frac_part.is_empty() {
            int_part.to_string()
        } else {
            format!("{int_part}{}{frac_part}", format.decimal_separator)
        };
        Ok(format!("{sign}{body}{exp_str}"))
    }

    /// Formats a finite length as a fixed decimal value.
    pub fn to_decimal(length: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(length, format, false)?;
        unit_format_math::fixed(
            length,
            places,
            &format.decimal_separator,
            format.suppress_linear_leading_zeros,
            format.suppress_linear_trailing_zeros,
        )
    }

    /// Formats inches as feet and decimal inches, carrying rounded inches into feet.
    pub fn to_engineering(length: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = unit_format_math::validate(length, format, false)?;
        let scale = 10u64.pow(places as u32);
        let rounded = unit_format_math::round_magnitude(length, scale);
        let (feet, inches_u64) = rounded.div_rem_u64(12 * scale);
        let inches_big = BigUint::from_u64(inches_u64);
        let text = unit_format_math::decimal(
            &inches_big,
            places,
            &format.decimal_separator,
            format.suppress_linear_leading_zeros,
            format.suppress_linear_trailing_zeros,
        );
        Ok(format!(
            "{}{}",
            Self::sign(length, &rounded),
            Self::feet_and_inches(&feet, &text, inches_u64 == 0, format)
        ))
    }

    /// Formats inches as feet and fractional inches, with normalized fractions and carries.
    pub fn to_architectural(length: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = Self::validate_fraction(length, format)?;
        let denominator = 1u64 << places;
        let rounded = unit_format_math::round_magnitude(length, denominator);
        let (feet, inch_units) = rounded.div_rem_u64(12 * denominator);
        let whole = inch_units / denominator;
        let numerator = inch_units % denominator;
        let inches = Self::fraction_text(&BigUint::from_u64(whole), numerator, denominator, format);
        let text = format!(
            "{}{}",
            Self::sign(length, &rounded),
            Self::feet_and_inches(&feet, &inches, inch_units == 0, format)
        );
        if numerator != 0 && format.fraction_type != FractionFormatType::NotStacked {
            Ok(format!("\\A1;{text}"))
        } else {
            Ok(text)
        }
    }

    /// Formats a length as a whole number and a reduced fraction.
    pub fn to_fractional(length: f64, format: &UnitStyleFormat) -> Result<String> {
        let places = Self::validate_fraction(length, format)?;
        let denominator = 1u64 << places;
        let rounded = unit_format_math::round_magnitude(length, denominator);
        let (whole, numerator) = rounded.div_rem_u64(denominator);
        let text = format!(
            "{}{}",
            Self::sign(length, &rounded),
            Self::fraction_text(&whole, numerator, denominator, format)
        );
        if numerator != 0 && format.fraction_type != FractionFormatType::NotStacked {
            Ok(format!("\\A1;{text}"))
        } else {
            Ok(text)
        }
    }

    fn validate_fraction(length: f64, format: &UnitStyleFormat) -> Result<usize> {
        let places = unit_format_math::validate(length, format, false)?;
        if !format.fraction_height_scale.is_finite() || format.fraction_height_scale <= 0.0 {
            return Err(DxfError::InvalidValue(
                "The fraction height scale must be finite and positive.".into(),
            ));
        }
        Ok(places)
    }

    fn sign(value: f64, rounded: &BigUint) -> &'static str {
        if value < 0.0 && !rounded.is_zero() {
            "-"
        } else {
            ""
        }
    }

    fn fraction_text(
        whole: &BigUint,
        numerator: u64,
        denominator: u64,
        format: &UnitStyleFormat,
    ) -> String {
        let text = whole.to_decimal_string();
        if numerator == 0 {
            return text;
        }
        let g = unit_format_math::gcd_u64(numerator, denominator);
        let n = numerator / g;
        let d = denominator / g;
        if format.fraction_type == FractionFormatType::NotStacked {
            return format!("{text} {n}/{d}");
        }
        let sep = if format.fraction_type == FractionFormatType::Diagonal {
            '#'
        } else {
            '/'
        };
        format!(
            "{text}{{\\H{}x;\\S{n}{sep}{d};}}",
            format.fraction_height_scale
        )
    }

    fn feet_and_inches(
        feet: &BigUint,
        inches: &str,
        zero_inches: bool,
        format: &UnitStyleFormat,
    ) -> String {
        if feet.is_zero() && format.suppress_zero_feet {
            return format!("{inches}{}", format.inches_symbol);
        }
        let text = format!("{}{}", feet.to_decimal_string(), format.feet_symbol);
        if zero_inches && format.suppress_zero_inches {
            return text;
        }
        format!(
            "{text}{}{inches}{}",
            format.feet_inches_separator, format.inches_symbol
        )
    }
}
