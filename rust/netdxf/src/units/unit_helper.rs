use crate::units::drawing_units::DrawingUnits;

/// Unit conversion helpers matching `netDxf.Units.UnitHelper`.
pub struct UnitHelper;

impl UnitHelper {
    pub fn conversion_factor(from: DrawingUnits, to: DrawingUnits) -> f64 {
        if from == to {
            return 1.0;
        }
        match (Self::to_meters_factor(from), Self::to_meters_factor(to)) {
            (Some(f), Some(t)) => f / t,
            _ => 1.0,
        }
    }

    pub fn convert(value: f64, from: DrawingUnits, to: DrawingUnits) -> f64 {
        value * Self::conversion_factor(from, to)
    }

    pub fn to_meters_factor(units: DrawingUnits) -> Option<f64> {
        Some(match units {
            DrawingUnits::Unitless => return None,
            DrawingUnits::Inches => 0.0254,
            DrawingUnits::Feet => 0.3048,
            DrawingUnits::Miles => 1609.344,
            DrawingUnits::Millimeters => 0.001,
            DrawingUnits::Centimeters => 0.01,
            DrawingUnits::Meters => 1.0,
            DrawingUnits::Kilometers => 1000.0,
            DrawingUnits::Microinches => 2.54e-8,
            DrawingUnits::Mils => 2.54e-5,
            DrawingUnits::Yards => 0.9144,
            DrawingUnits::Angstroms => 1e-10,
            DrawingUnits::Nanometers => 1e-9,
            DrawingUnits::Microns => 1e-6,
            DrawingUnits::Decimeters => 0.1,
            DrawingUnits::Decameters => 10.0,
            DrawingUnits::Hectometers => 100.0,
            DrawingUnits::Gigameters => 1e9,
            DrawingUnits::AstronomicalUnits => 149_597_870_700.0,
            DrawingUnits::LightYears => 9_460_730_472_580_800.0,
            DrawingUnits::Parsecs => 30_856_775_814_913_673.0,
            DrawingUnits::USSurveyFeet => 1200.0 / 3937.0,
            DrawingUnits::USSurveyInches => 100.0 / 3937.0,
            DrawingUnits::USSurveyYards => 3600.0 / 3937.0,
            DrawingUnits::USSurveyMiles => 6_336_000.0 / 3937.0,
        })
    }
}
