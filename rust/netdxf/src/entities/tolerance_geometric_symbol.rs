/// Defines the geometric characteristic symbols for location, orientation, form, profile, and runout.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Default)]
pub enum ToleranceGeometricSymbol {
    #[default]
    None,
    Position,
    Concentricity,
    Symmetry,
    Parallelism,
    Perpendicularity,
    Angularity,
    Cylindricity,
    Flatness,
    Roundness,
    Straightness,
    ProfileSurface,
    ProfileLine,
    CircularRunout,
    TotalRunOut,
}
