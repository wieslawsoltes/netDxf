/// Angle unit format (`$AUNITS` / `DIMAU`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum AngleUnitType {
    #[default]
    DecimalDegrees = 0,
    DegreesMinutesSeconds = 1,
    Gradians = 2,
    Radians = 3,
    SurveyorUnits = 4,
}

impl AngleUnitType {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::DecimalDegrees,
            1 => Self::DegreesMinutesSeconds,
            2 => Self::Gradians,
            3 => Self::Radians,
            4 => Self::SurveyorUnits,
            _ => return None,
        })
    }
}
