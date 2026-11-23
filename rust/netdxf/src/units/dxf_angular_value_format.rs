use crate::error::{DxfError, Result};
use crate::math_helper::MathHelper;

/// Parsed DXF angular value formatter (`netDxf.Units.DxfAngularValueFormat`).
///
/// Supports `%au0..4` (decimal degrees, DMS, gradians, radians, surveyor's bearings),
/// `%pr0..8`, `%ps[prefix,suffix]`, `%ds<ascii>`, and `%zs<mask>`.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfAngularValueFormat {
    pub expression: String,
    pub angular_unit: u8,
    pub precision: u8,
    pub prefix: String,
    pub suffix: String,
    pub decimal_separator: char,
    pub zero_suppression: u8,
}

impl DxfAngularValueFormat {
    pub fn parse(expression: &str) -> Result<Self> {
        if expression.is_empty() || !expression.starts_with('%') || expression.contains('\0') {
            return Err(DxfError::InvalidValue(format!(
                "Invalid angular value format: '{expression}'"
            )));
        }
        let mut au: Option<u8> = None;
        let mut pr: Option<u8> = None;
        let mut prefix = String::new();
        let mut suffix = String::new();
        let mut decimal_separator = '.';
        let mut zero_suppression = 0u8;

        let bytes = expression.as_bytes();
        let mut pos = 0;
        while pos < bytes.len() {
            if bytes[pos] != b'%' || pos + 3 > bytes.len() {
                return Err(DxfError::InvalidValue(format!(
                    "Invalid angular directive in '{expression}'"
                )));
            }
            pos += 1;
            let tag = &expression[pos..pos + 2];
            pos += 2;
            match tag {
                "au" => {
                    if au.is_some() {
                        return Err(DxfError::InvalidValue("Duplicate %au directive".into()));
                    }
                    let v = Self::read_uint(expression, &mut pos)?;
                    if v > 4 {
                        return Err(DxfError::InvalidValue(format!("Invalid %au value: {v}")));
                    }
                    au = Some(v as u8);
                }
                "pr" => {
                    if pr.is_some() {
                        return Err(DxfError::InvalidValue("Duplicate %pr directive".into()));
                    }
                    let v = Self::read_uint(expression, &mut pos)?;
                    if v > 8 {
                        return Err(DxfError::InvalidValue(format!("Invalid %pr value: {v}")));
                    }
                    pr = Some(v as u8);
                }
                "ds" => {
                    let v = Self::read_uint(expression, &mut pos)?;
                    if !(33..=126).contains(&v) {
                        return Err(DxfError::InvalidValue(format!("Invalid %ds ascii: {v}")));
                    }
                    decimal_separator = v as u8 as char;
                }
                "zs" => {
                    let v = Self::read_uint(expression, &mut pos)?;
                    if !matches!(v, 0 | 4 | 8 | 12) {
                        return Err(DxfError::InvalidValue(format!("Invalid %zs mask: {v}")));
                    }
                    zero_suppression = v as u8;
                }
                "ps" => {
                    if bytes.get(pos) != Some(&b'[') {
                        return Err(DxfError::InvalidValue("Expected '[' after %ps".into()));
                    }
                    pos += 1;
                    let start = pos;
                    while pos < bytes.len() && bytes[pos] != b']' {
                        pos += 1;
                    }
                    if bytes.get(pos) != Some(&b']') {
                        return Err(DxfError::InvalidValue("Unclosed %ps".into()));
                    }
                    let content = &expression[start..pos];
                    pos += 1;
                    let parts: Vec<&str> = content.split(',').collect();
                    if parts.len() != 2 || content.contains('[') || content.contains(']') {
                        return Err(DxfError::InvalidValue("Invalid %ps payload".into()));
                    }
                    prefix = parts[0].to_string();
                    suffix = parts[1].to_string();
                }
                _ => {
                    return Err(DxfError::InvalidValue(format!(
                        "Unsupported angular directive '%{tag}'"
                    )));
                }
            }
        }
        let angular_unit =
            au.ok_or_else(|| DxfError::InvalidValue("Missing %au directive".into()))?;
        let precision = pr.ok_or_else(|| DxfError::InvalidValue("Missing %pr directive".into()))?;
        if matches!(angular_unit, 1 | 4) && zero_suppression != 0 {
            return Err(DxfError::InvalidValue(
                "Zero suppression is not supported for DMS or surveyor bearings".into(),
            ));
        }
        Ok(Self {
            expression: expression.to_string(),
            angular_unit,
            precision,
            prefix,
            suffix,
            decimal_separator,
            zero_suppression,
        })
    }

    fn read_uint(expr: &str, pos: &mut usize) -> Result<u32> {
        let start = *pos;
        let bytes = expr.as_bytes();
        while *pos < bytes.len() && bytes[*pos].is_ascii_digit() {
            *pos += 1;
        }
        if *pos == start {
            return Err(DxfError::InvalidValue(format!(
                "Expected integer in '{expr}'"
            )));
        }
        expr[start..*pos]
            .parse::<u32>()
            .map_err(|_| DxfError::InvalidValue(format!("Invalid integer in '{expr}'")))
    }

    /// Formats an angle given in radians.
    pub fn format_radians(&self, radians: f64) -> Result<String> {
        if !radians.is_finite() {
            return Err(DxfError::InvalidValue(
                "Cannot format non-finite angle".to_string(),
            ));
        }
        let deg = radians * MathHelper::RAD_TO_DEG;
        let body = match self.angular_unit {
            0 => self.format_scalar(deg),
            1 => Self::format_dms(deg, self.precision),
            2 => self.format_scalar(deg * MathHelper::DEG_TO_GRAD),
            3 => self.format_scalar(radians),
            4 => Self::format_bearing(deg, self.precision),
            _ => unreachable!(),
        };
        Ok(format!("{}{body}{}", self.prefix, self.suffix))
    }

    fn format_scalar(&self, value: f64) -> String {
        let pr = self.precision as usize;
        let factor = 10_f64.powi(self.precision as i32);
        // Ties-to-even rounding matching IEEE / .NET MidpointRounding.ToEven
        let rounded = (value * factor).round_ties_even() / factor;
        let v = if rounded == 0.0 { 0.0 } else { rounded };
        let neg = v.is_sign_negative() && v != 0.0;
        let raw = format!("{:.pr$}", v.abs());
        let (int_part, frac_part) = raw.split_once('.').unwrap_or((&raw, ""));
        let frac = if (self.zero_suppression & 8) != 0 {
            frac_part.trim_end_matches('0')
        } else {
            frac_part
        };
        let body = if frac.is_empty() {
            int_part.to_string()
        } else if (self.zero_suppression & 4) != 0 && int_part == "0" {
            format!("{}{}", self.decimal_separator, frac)
        } else {
            format!("{int_part}{}{frac}", self.decimal_separator)
        };
        if neg {
            format!("-{body}")
        } else {
            body
        }
    }

    fn format_dms(deg: f64, pr: u8) -> String {
        let neg = deg < 0.0;
        let abs_deg = deg.abs();
        let d = abs_deg.floor() as u64;
        let rem_min = (abs_deg - d as f64) * 60.0;
        let m = rem_min.floor() as u64;
        let s = (rem_min - m as f64) * 60.0;
        let sign = if neg { "-" } else { "" };
        if pr == 0 {
            format!("{sign}{d}°")
        } else if pr <= 2 {
            format!("{sign}{d}°{m}'")
        } else {
            let sec_int = s.round() as u64;
            format!("{sign}{d}°{m}'{sec_int}\"")
        }
    }

    fn format_bearing(deg: f64, pr: u8) -> String {
        let norm = MathHelper::normalize_angle(deg);
        let (ns, ew, angle) = if norm <= 90.0 {
            ("N", "E", 90.0 - norm)
        } else if norm <= 180.0 {
            ("N", "W", norm - 90.0)
        } else if norm <= 270.0 {
            ("S", "W", 270.0 - norm)
        } else {
            ("S", "E", norm - 270.0)
        };
        format!("{ns} {} {ew}", Self::format_dms(angle, pr))
    }
}
