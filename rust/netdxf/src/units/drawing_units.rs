/// Default drawing units for AutoCAD DesignCenter blocks (`$INSUNITS`).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Hash)]
#[repr(i16)]
pub enum DrawingUnits {
    #[default]
    Unitless = 0,
    Inches = 1,
    Feet = 2,
    Miles = 3,
    Millimeters = 4,
    Centimeters = 5,
    Meters = 6,
    Kilometers = 7,
    Microinches = 8,
    Mils = 9,
    Yards = 10,
    Angstroms = 11,
    Nanometers = 12,
    Microns = 13,
    Decimeters = 14,
    Decameters = 15,
    Hectometers = 16,
    Gigameters = 17,
    AstronomicalUnits = 18,
    LightYears = 19,
    Parsecs = 20,
    USSurveyFeet = 21,
    USSurveyInches = 22,
    USSurveyYards = 23,
    USSurveyMiles = 24,
}

impl DrawingUnits {
    pub fn from_i16(v: i16) -> Option<Self> {
        Some(match v {
            0 => Self::Unitless,
            1 => Self::Inches,
            2 => Self::Feet,
            3 => Self::Miles,
            4 => Self::Millimeters,
            5 => Self::Centimeters,
            6 => Self::Meters,
            7 => Self::Kilometers,
            8 => Self::Microinches,
            9 => Self::Mils,
            10 => Self::Yards,
            11 => Self::Angstroms,
            12 => Self::Nanometers,
            13 => Self::Microns,
            14 => Self::Decimeters,
            15 => Self::Decameters,
            16 => Self::Hectometers,
            17 => Self::Gigameters,
            18 => Self::AstronomicalUnits,
            19 => Self::LightYears,
            20 => Self::Parsecs,
            21 => Self::USSurveyFeet,
            22 => Self::USSurveyInches,
            23 => Self::USSurveyYards,
            24 => Self::USSurveyMiles,
            _ => return None,
        })
    }
}
