namespace netDxf.Shadow.Geometry;

/// <summary>Detached drawing-unit conversion matching netDxf's public UnitHelper factors.
/// Unitless on either side means no implicit conversion.</summary>
public static class DrawingUnitConversion
{
    public static double Factor(string source, string destination)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination) || source == "Unitless" || destination == "Unitless") return 1;
        return (double)(Meters(source) / Meters(destination));
    }

    private static decimal Meters(string unit) => unit switch
    {
        "Inches" => .0254m, "Feet" => .3048m, "Miles" => 1609.344m, "Millimeters" => .001m, "Centimeters" => .01m,
        "Meters" => 1m, "Kilometers" => 1000m, "Microinches" => .0000000254m, "Mils" => .0000254m, "Yards" => .9144m,
        "Angstroms" => .0000000001m, "Nanometers" => .000000001m, "Microns" => .000001m, "Decimeters" => .1m,
        "Decameters" => 10m, "Hectometers" => 100m, "Gigameters" => 1000000000m, "AstronomicalUnits" => 149597900000m,
        "LightYears" => 9460732325559000m, "Parsecs" => 30856781858520240m,
        "USSurveyFeet" => .3048006096m, "USSurveyInches" => .0254000508m, "USSurveyYards" => .9144018288m, "USSurveyMiles" => 1609.3472187m,
        _ => throw new NotSupportedException($"Unknown insertion units '{unit}'.")
    };
}
