use crate::error::{DxfError, Result};

/// Parsed DXF field / table scalar value formatter (`netDxf.Units.DxfValueFormat`).
///
/// Supports `%lu1..5` (scientific, decimal, engineering, architectural, fractional),
/// `%pr0..8`, `%ps[prefix,suffix]`, `%ct8[factor]`, `%ds<ascii>`, `%th<ascii>`, and `%zs<mask>`.
#[derive(Clone, Debug, PartialEq)]
pub struct DxfValueFormat {
    pub expression: String,
    pub linear_unit: Option<u8>,
    pub precision: Option<u8>,
    pub prefix: String,
    pub suffix: String,
    pub factor: f64,
    pub decimal_separator: char,
    pub thousands_separator: Option<char>,
    pub zero_suppression: u8,
}

impl DxfValueFormat {
    pub const MAXIMUM_EXPRESSION_LENGTH: usize = 4096;
    pub const MAXIMUM_LENGTH: usize = 4096;

    pub fn try_parse(expression: &str) -> Option<Self> {
        Self::parse(expression).ok()
    }

    pub fn format(&self, value: f64) -> Result<String> {
        self.format_f64(value)
    }

    pub fn parse(expression: &str) -> Result<Self> {
        if expression.len() > Self::MAXIMUM_EXPRESSION_LENGTH || expression.contains('\0') {
            return Err(DxfError::InvalidValue(
                "Invalid DXF value format expression".to_string(),
            ));
        }
        let mut fmt = Self {
            expression: expression.to_string(),
            linear_unit: None,
            precision: None,
            prefix: String::new(),
            suffix: String::new(),
            factor: 1.0,
            decimal_separator: '.',
            thousands_separator: None,
            zero_suppression: 0,
        };
        if expression.is_empty() {
            return Ok(fmt);
        }
        if !expression.starts_with('%') {
            return Err(DxfError::InvalidValue(format!(
                "Value format must start with '%': '{expression}'"
            )));
        }

        let bytes = expression.as_bytes();
        let mut pos = 0;
        let mut seen_lu = false;
        let mut seen_pr = false;
        let mut seen_ds = false;
        let mut seen_th = false;
        let mut seen_ps = false;
        let mut seen_ct = false;
        let mut seen_zs = false;

        while pos < bytes.len() {
            if bytes[pos] != b'%' {
                return Err(DxfError::InvalidValue(format!(
                    "Unexpected characters in format '{expression}'"
                )));
            }
            pos += 1;
            if pos + 2 > bytes.len() {
                return Err(DxfError::InvalidValue(format!(
                    "Truncated directive in format '{expression}'"
                )));
            }
            let tag = &expression[pos..pos + 2];
            pos += 2;
            match tag {
                "lu" => {
                    if seen_lu {
                        return Err(DxfError::InvalidValue("Duplicate %lu directive".into()));
                    }
                    seen_lu = true;
                    let d = Self::read_uint(expression, &mut pos)?;
                    if !(1..=5).contains(&d) {
                        return Err(DxfError::InvalidValue(format!("Invalid %lu value: {d}")));
                    }
                    fmt.linear_unit = Some(d as u8);
                }
                "pr" => {
                    if seen_pr {
                        return Err(DxfError::InvalidValue("Duplicate %pr directive".into()));
                    }
                    seen_pr = true;
                    let d = Self::read_uint(expression, &mut pos)?;
                    if d > 8 {
                        return Err(DxfError::InvalidValue(format!("Invalid %pr value: {d}")));
                    }
                    fmt.precision = Some(d as u8);
                }
                "ds" => {
                    if seen_ds {
                        return Err(DxfError::InvalidValue("Duplicate %ds directive".into()));
                    }
                    seen_ds = true;
                    let code = Self::read_uint(expression, &mut pos)?;
                    if !(33..=126).contains(&code) {
                        return Err(DxfError::InvalidValue(format!("Invalid %ds ascii: {code}")));
                    }
                    fmt.decimal_separator = code as u8 as char;
                }
                "th" => {
                    if seen_th {
                        return Err(DxfError::InvalidValue("Duplicate %th directive".into()));
                    }
                    seen_th = true;
                    let code = Self::read_uint(expression, &mut pos)?;
                    if !(33..=126).contains(&code) {
                        return Err(DxfError::InvalidValue(format!("Invalid %th ascii: {code}")));
                    }
                    fmt.thousands_separator = Some(code as u8 as char);
                }
                "zs" => {
                    if seen_zs {
                        return Err(DxfError::InvalidValue("Duplicate %zs directive".into()));
                    }
                    seen_zs = true;
                    let mask = Self::read_uint(expression, &mut pos)?;
                    if !matches!(mask, 0 | 4 | 8 | 12) {
                        return Err(DxfError::InvalidValue(format!("Invalid %zs mask: {mask}")));
                    }
                    fmt.zero_suppression = mask as u8;
                }
                "ps" => {
                    if seen_ps {
                        return Err(DxfError::InvalidValue("Duplicate %ps directive".into()));
                    }
                    seen_ps = true;
                    let content = Self::read_bracketed(expression, &mut pos)?;
                    let parts: Vec<&str> = content.split(',').collect();
                    if parts.len() != 2 || content.contains('[') || content.contains(']') {
                        return Err(DxfError::InvalidValue(format!(
                            "Invalid %ps payload: '{content}'"
                        )));
                    }
                    fmt.prefix = parts[0].to_string();
                    fmt.suffix = parts[1].to_string();
                }
                "ct" => {
                    if seen_ct {
                        return Err(DxfError::InvalidValue("Duplicate %ct directive".into()));
                    }
                    seen_ct = true;
                    let kind = Self::read_uint(expression, &mut pos)?;
                    if kind != 8 {
                        return Err(DxfError::InvalidValue(format!(
                            "Unsupported %ct kind: {kind}"
                        )));
                    }
                    let raw = Self::read_bracketed(expression, &mut pos)?;
                    if raw.is_empty() || raw.bytes().any(|b| b.is_ascii_whitespace()) {
                        return Err(DxfError::InvalidValue(format!(
                            "Invalid %ct8 factor: '{raw}'"
                        )));
                    }
                    let factor: f64 = raw.parse().map_err(|_| {
                        DxfError::InvalidValue(format!("Invalid %ct8 factor: '{raw}'"))
                    })?;
                    if !factor.is_finite() {
                        return Err(DxfError::InvalidValue(format!(
                            "Non-finite %ct8 factor: '{raw}'"
                        )));
                    }
                    fmt.factor = factor;
                }
                _ => {
                    return Err(DxfError::InvalidValue(format!(
                        "Unsupported format directive '%{tag}'"
                    )));
                }
            }
        }

        if seen_lu != seen_pr {
            return Err(DxfError::InvalidValue(
                "%lu and %pr must be specified together".to_string(),
            ));
        }
        if seen_ds && fmt.thousands_separator == Some(fmt.decimal_separator) {
            return Err(DxfError::InvalidValue(
                "Decimal and thousands separators must differ".to_string(),
            ));
        }
        if fmt.linear_unit == Some(5) && seen_ds {
            return Err(DxfError::InvalidValue(
                "Fractional format does not accept %ds".to_string(),
            ));
        }
        Ok(fmt)
    }

    fn read_uint(expr: &str, pos: &mut usize) -> Result<u32> {
        let start = *pos;
        let bytes = expr.as_bytes();
        while *pos < bytes.len() && bytes[*pos].is_ascii_digit() {
            *pos += 1;
        }
        if *pos == start {
            return Err(DxfError::InvalidValue(format!(
                "Expected unsigned integer in format '{expr}'"
            )));
        }
        expr[start..*pos]
            .parse::<u32>()
            .map_err(|_| DxfError::InvalidValue(format!("Integer overflow in format '{expr}'")))
    }

    fn read_bracketed<'a>(expr: &'a str, pos: &mut usize) -> Result<&'a str> {
        let bytes = expr.as_bytes();
        if bytes.get(*pos) != Some(&b'[') {
            return Err(DxfError::InvalidValue(format!(
                "Expected '[' in format '{expr}'"
            )));
        }
        *pos += 1;
        let start = *pos;
        while *pos < bytes.len() && bytes[*pos] != b']' {
            *pos += 1;
        }
        if bytes.get(*pos) != Some(&b']') {
            return Err(DxfError::InvalidValue(format!(
                "Unclosed '[' in format '{expr}'"
            )));
        }
        let slice = &expr[start..*pos];
        *pos += 1;
        Ok(slice)
    }

    /// Formats a finite `f64` value according to this `DxfValueFormat`.
    pub fn format_f64(&self, value: f64) -> Result<String> {
        if !value.is_finite() {
            return Err(DxfError::InvalidValue(
                "Cannot format non-finite floating-point value".to_string(),
            ));
        }
        let scaled = value * self.factor;
        if !scaled.is_finite() {
            return Err(DxfError::InvalidValue(
                "Scaled value overflowed f64".to_string(),
            ));
        }
        let core = match (self.linear_unit, self.precision) {
            (None, None) => {
                if scaled == 0.0 {
                    "0".to_string()
                } else {
                    format!("{scaled}")
                }
            }
            (Some(1), Some(pr)) => Self::format_scientific(scaled, pr as usize, self.decimal_separator),
            (Some(2), Some(pr)) => self.format_decimal(scaled, pr as usize),
            (Some(3), Some(pr)) => self.format_engineering(scaled, pr as usize),
            (Some(4), Some(pr)) => Self::format_architectural(scaled, pr as usize),
            (Some(5), Some(pr)) => Self::format_fractional(scaled, pr as usize),
            _ => format!("{scaled}"),
        };
        Ok(format!("{}{core}{}", self.prefix, self.suffix))
    }

    /// Formats a string value with optional prefix/suffix (only allowed when no numeric `%lu` is set).
    pub fn format_str(&self, value: &str) -> Result<String> {
        if self.linear_unit.is_some() {
            return Err(DxfError::InvalidValue(
                "Numeric format cannot format a string value".to_string(),
            ));
        }
        Ok(format!("{}{value}{}", self.prefix, self.suffix))
    }

    fn format_scientific(value: f64, pr: usize, dec_sep: char) -> String {
        let s = format!("{value:.pr$E}");
        // Rust produces e.g. "1.7500E1" or "1.25E-3"; DXF uses 2-digit signed exponent "E+01" / "E-03".
        if let Some((mant, exp_str)) = s.split_once('E') {
            let exp: i32 = exp_str.parse().unwrap_or(0);
            let sign = if exp < 0 { '-' } else { '+' };
            let m = if dec_sep != '.' {
                mant.replace('.', &dec_sep.to_string())
            } else {
                mant.to_string()
            };
            format!("{m}E{sign}{:02}", exp.abs())
        } else {
            s
        }
    }

    fn format_decimal(&self, value: f64, pr: usize) -> String {
        let factor = 10_f64.powi(pr as i32);
        let mut rounded = (value * factor).round() / factor;
        if rounded == 0.0 {
            rounded = 0.0;
        }
        let neg = rounded.is_sign_negative() && rounded != 0.0;
        let abs_val = rounded.abs();
        let raw = format!("{abs_val:.pr$}");
        let (int_part, frac_part) = raw.split_once('.').unwrap_or((&raw, ""));

        let int_with_th = if let Some(th) = self.thousands_separator {
            Self::insert_thousands(int_part, th)
        } else {
            int_part.to_string()
        };

        let suppress_leading = (self.zero_suppression & 4) != 0;
        let suppress_trailing = (self.zero_suppression & 8) != 0;

        let frac_trimmed = if suppress_trailing {
            frac_part.trim_end_matches('0')
        } else {
            frac_part
        };

        let body = if frac_trimmed.is_empty() {
            int_with_th
        } else if suppress_leading && int_with_th == "0" {
            format!("{}{}", self.decimal_separator, frac_trimmed)
        } else {
            format!("{int_with_th}{}{frac_trimmed}", self.decimal_separator)
        };

        if neg {
            format!("-{body}")
        } else {
            body
        }
    }

    fn format_engineering(&self, value: f64, pr: usize) -> String {
        let neg = value < 0.0;
        let abs_in = value.abs();
        let factor = 10_f64.powi(pr as i32);
        let total_in = (abs_in * factor).round() / factor;
        let feet = (total_in / 12.0).floor() as u64;
        let rem_in = total_in - (feet as f64) * 12.0;
        let sign = if neg && total_in > 0.0 { "-" } else { "" };
        format!("{sign}{feet}'-{rem_in:.pr$}\"")
    }

    fn format_architectural(value: f64, pr: usize) -> String {
        let neg = value < 0.0;
        let denom = 1u64 << pr.min(8);
        let total_units = (value.abs() * denom as f64).round() as u64;
        let whole_in = total_units / denom;
        let rem_num = total_units % denom;
        let feet = whole_in / 12;
        let inches = whole_in % 12;
        let sign = if neg && total_units > 0 { "-" } else { "" };
        if rem_num == 0 {
            format!("{sign}{feet}'-{inches}\"")
        } else {
            let g = Self::gcd(rem_num, denom);
            format!("{sign}{feet}'-{inches} {}/{}\"", rem_num / g, denom / g)
        }
    }

    fn format_fractional(value: f64, pr: usize) -> String {
        let neg = value < 0.0;
        let denom = 1u64 << pr.min(8);
        let total_units = (value.abs() * denom as f64).round() as u64;
        let whole = total_units / denom;
        let rem_num = total_units % denom;
        let sign = if neg && total_units > 0 { "-" } else { "" };
        if rem_num == 0 {
            format!("{sign}{whole}")
        } else {
            let g = Self::gcd(rem_num, denom);
            if whole == 0 {
                format!("{sign}{}/{}", rem_num / g, denom / g)
            } else {
                format!("{sign}{whole} {}/{}", rem_num / g, denom / g)
            }
        }
    }

    fn insert_thousands(int_part: &str, sep: char) -> String {
        let mut out = String::with_capacity(int_part.len() + int_part.len() / 3);
        let len = int_part.len();
        for (i, ch) in int_part.chars().enumerate() {
            if i > 0 && (len - i) % 3 == 0 {
                out.push(sep);
            }
            out.push(ch);
        }
        out
    }

    fn gcd(mut a: u64, mut b: u64) -> u64 {
        while b != 0 {
            let t = b;
            b = a % b;
            a = t;
        }
        a.max(1)
    }
}
