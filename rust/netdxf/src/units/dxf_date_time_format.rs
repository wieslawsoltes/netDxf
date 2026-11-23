use crate::error::{DxfError, Result};
use crate::units::drawing_time::DrawingTime;

/// Civil date-time tuple `(year, month, day, hour, minute, second)` for timezone-free DXF date formatting.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct CivilDateTime {
    pub year: i32,
    pub month: u32,
    pub day: u32,
    pub hour: u32,
    pub minute: u32,
    pub second: u32,
}

impl CivilDateTime {
    pub const fn new(
        year: i32,
        month: u32,
        day: u32,
        hour: u32,
        minute: u32,
        second: u32,
    ) -> Self {
        Self {
            year,
            month,
            day,
            hour,
            minute,
            second,
        }
    }

    /// Converts a DXF Julian calendar date (`DrawingTime`) to `CivilDateTime`.
    pub fn from_julian(julian: f64) -> Result<Self> {
        if !julian.is_finite() {
            return Err(DxfError::InvalidValue(
                "Julian date must be finite".to_string(),
            ));
        }
        let st = DrawingTime::from_julian_calendar(julian)?;
        let unix_secs = match st.duration_since(std::time::UNIX_EPOCH) {
            Ok(d) => d.as_secs() as i64,
            Err(e) => -(e.duration().as_secs() as i64),
        };
        Ok(Self::from_unix_seconds(unix_secs))
    }

    pub fn from_unix_seconds(unix_secs: i64) -> Self {
        let days = unix_secs.div_euclid(86_400);
        let rem = unix_secs.rem_euclid(86_400) as u32;
        let hour = rem / 3600;
        let minute = (rem % 3600) / 60;
        let second = rem % 60;

        // Howard Hinnant's civil_from_days algorithm (exact Gregorian calendar)
        let z = days + 719_468;
        let era = z.div_euclid(146_097);
        let doe = (z - era * 146_097) as u64;
        let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
        let y = yoe as i64 + era * 400;
        let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        let mp = (5 * doy + 2) / 153;
        let d = doy - (153 * mp + 2) / 5 + 1;
        let m = if mp < 10 { mp + 3 } else { mp - 9 };
        let year = (y + i64::from(m <= 2)) as i32;
        Self::new(year, m as u32, d as u32, hour, minute, second)
    }
}

/// Parsed DXF date-time formatter (`netDxf.Units.DxfDateTimeFormat`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct DxfDateTimeFormat {
    pub mask: String,
}

impl DxfDateTimeFormat {
    pub const MAXIMUM_MASK_LENGTH: usize = 4096;
    pub const MAXIMUM_LENGTH: usize = 4096;

    #[inline]
    pub fn expression(&self) -> &str {
        &self.mask
    }

    pub fn parse(mask: &str) -> Result<Self> {
        if mask.len() > Self::MAXIMUM_MASK_LENGTH || mask.contains('\0') {
            return Err(DxfError::InvalidValue(
                "Invalid DXF date-time mask".to_string(),
            ));
        }
        // Validate quotes and escapes
        let mut chars = mask.chars().peekable();
        while let Some(c) = chars.next() {
            match c {
                '\'' => {
                    let mut closed = false;
                    while let Some(nc) = chars.next() {
                        if nc == '\'' {
                            if chars.peek() == Some(&'\'') {
                                chars.next();
                            } else {
                                closed = true;
                                break;
                            }
                        }
                    }
                    if !closed {
                        return Err(DxfError::InvalidValue(
                            "Unclosed quoted literal in date mask".to_string(),
                        ));
                    }
                }
                '\\' => {
                    if chars.next().is_none() {
                        return Err(DxfError::InvalidValue(
                            "Trailing escape '\\' in date mask".to_string(),
                        ));
                    }
                }
                _ => {}
            }
        }
        Ok(Self {
            mask: mask.to_string(),
        })
    }

    pub fn format(&self, dt: CivilDateTime) -> String {
        let mut out = String::new();
        let chars: Vec<char> = self.mask.chars().collect();
        let mut i = 0;
        while i < chars.len() {
            let c = chars[i];
            if c == '\'' {
                if chars.get(i + 1) == Some(&'\'') {
                    out.push('\'');
                    i += 2;
                    continue;
                }
                i += 1;
                while i < chars.len() {
                    if chars[i] == '\'' {
                        if chars.get(i + 1) == Some(&'\'') {
                            out.push('\'');
                            i += 2;
                        } else {
                            i += 1;
                            break;
                        }
                    } else {
                        out.push(chars[i]);
                        i += 1;
                    }
                }
                continue;
            }
            if c == '\\' && i + 1 < chars.len() {
                out.push(chars[i + 1]);
                i += 2;
                continue;
            }
            let run_start = i;
            while i < chars.len() && chars[i] == c {
                i += 1;
            }
            let count = i - run_start;
            match (c, count) {
                ('y', 1) => out.push_str(&format!("{}", dt.year % 100)),
                ('y', 2) => out.push_str(&format!("{:02}", dt.year % 100)),
                ('y', 3) => out.push_str(&format!("{:04}", dt.year)),
                ('y', 4) => out.push_str(&format!("{:04}", dt.year)),
                ('M', 1) => out.push_str(&format!("{}", dt.month)),
                ('M', 2) => out.push_str(&format!("{:02}", dt.month)),
                ('M', 3) => out.push_str(Self::month_abbr(dt.month)),
                ('M', 4) => out.push_str(Self::month_full(dt.month)),
                ('d', 1) => out.push_str(&format!("{}", dt.day)),
                ('d', 2) => out.push_str(&format!("{:02}", dt.day)),
                ('H', 1) => out.push_str(&format!("{}", dt.hour)),
                ('H', 2) => out.push_str(&format!("{:02}", dt.hour)),
                ('h', 1) => out.push_str(&format!("{}", Self::hour12(dt.hour))),
                ('h', 2) => out.push_str(&format!("{:02}", Self::hour12(dt.hour))),
                ('m', 1) => out.push_str(&format!("{}", dt.minute)),
                ('m', 2) => out.push_str(&format!("{:02}", dt.minute)),
                ('s', 1) => out.push_str(&format!("{}", dt.second)),
                ('s', 2) => out.push_str(&format!("{:02}", dt.second)),
                ('t', 1) => out.push_str(if dt.hour < 12 { "A" } else { "P" }),
                ('t', 2) => out.push_str(if dt.hour < 12 { "AM" } else { "PM" }),
                _ => {
                    for _ in 0..count {
                        out.push(c);
                    }
                }
            }
        }
        out
    }

    pub fn format_julian(&self, julian: f64) -> Result<String> {
        Ok(self.format(CivilDateTime::from_julian(julian)?))
    }

    const fn hour12(hour: u32) -> u32 {
        match hour % 12 {
            0 => 12,
            h => h,
        }
    }

    const fn month_abbr(month: u32) -> &'static str {
        match month {
            1 => "Jan",
            2 => "Feb",
            3 => "Mar",
            4 => "Apr",
            5 => "May",
            6 => "Jun",
            7 => "Jul",
            8 => "Aug",
            9 => "Sep",
            10 => "Oct",
            11 => "Nov",
            12 => "Dec",
            _ => "",
        }
    }

    const fn month_full(month: u32) -> &'static str {
        match month {
            1 => "January",
            2 => "February",
            3 => "March",
            4 => "April",
            5 => "May",
            6 => "June",
            7 => "July",
            8 => "August",
            9 => "September",
            10 => "October",
            11 => "November",
            12 => "December",
            _ => "",
        }
    }
}
