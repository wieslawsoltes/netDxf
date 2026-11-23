use std::time::{Duration, SystemTime, UNIX_EPOCH};

use crate::error::{DxfError, Result};

/// Converts DXF midnight-based Julian day serials and elapsed-day values.
pub struct DrawingTime;

impl DrawingTime {
    pub const MINIMUM_JULIAN_DATE: f64 = 1_721_426.0;
    pub const MAXIMUM_JULIAN_DATE_EXCLUSIVE: f64 = 5_373_485.0;
    pub const UNIX_EPOCH_JULIAN_DATE: f64 = 2_440_588.0;
    pub const SECONDS_PER_DAY: f64 = 86_400.0;

    pub fn to_julian_calendar(time: SystemTime) -> f64 {
        match time.duration_since(UNIX_EPOCH) {
            Ok(d) => Self::UNIX_EPOCH_JULIAN_DATE + d.as_secs_f64() / Self::SECONDS_PER_DAY,
            Err(e) => {
                Self::UNIX_EPOCH_JULIAN_DATE - e.duration().as_secs_f64() / Self::SECONDS_PER_DAY
            }
        }
    }

    pub fn from_julian_calendar(julian: f64) -> Result<SystemTime> {
        if !julian.is_finite()
            || !(Self::MINIMUM_JULIAN_DATE..Self::MAXIMUM_JULIAN_DATE_EXCLUSIVE).contains(&julian)
        {
            return Err(DxfError::InvalidValue(format!(
                "Julian date {julian} is out of valid DXF range"
            )));
        }
        let secs = (julian - Self::UNIX_EPOCH_JULIAN_DATE) * Self::SECONDS_PER_DAY;
        if secs >= 0.0 {
            Ok(UNIX_EPOCH + Duration::from_secs_f64(secs))
        } else {
            Ok(UNIX_EPOCH - Duration::from_secs_f64(-secs))
        }
    }

    pub fn editing_time(elapsed_days: f64) -> Result<Duration> {
        if !elapsed_days.is_finite() || elapsed_days < 0.0 {
            return Err(DxfError::InvalidValue(format!(
                "Invalid elapsed days value: {elapsed_days}"
            )));
        }
        Ok(Duration::from_secs_f64(elapsed_days * Self::SECONDS_PER_DAY))
    }

    pub fn to_elapsed_days(duration: Duration) -> f64 {
        duration.as_secs_f64() / Self::SECONDS_PER_DAY
    }
}
